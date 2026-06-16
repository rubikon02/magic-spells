"""
Spell gesture recognition — 1-D Convolutional Neural Network (PyTorch).

Architecture  : 3-block Conv1d encoder → Global-Avg-Pool → 2-FC head
Input         : (batch, 14, 64) float32 [14 channels × 64 frames]
Output (ONNX) : (batch, 6)   float32  class probabilities (softmax applied)

Preprocessing pipeline (mirror this in Unity C# for real-time inference):
  1. Collect controller frames for the gesture (any length >= 4).
  2. Linearly resample to exactly N_FRAMES = 64 frames.
  3. Subtract the first-frame position from every frame
       — left hand  : channels at indices [0, 1, 2]
       — right hand : channels at indices [7, 8, 9]
  4. Feed into the ONNX model as a 3D tensor [1, 14, 64].
  5. Output is a float[6] probability vector; argmax → spell index.

Label mapping (also saved in models/labels.json):
  0 lightning · 1 fireball · 2 wingardium_leviosa
  3 lumos     · 4 open_door · 5 knock_off
"""

import json
import warnings
from pathlib import Path

import numpy as np
import pandas as pd
from scipy.interpolate import interp1d
from sklearn.metrics import (
    accuracy_score,
    classification_report,
    cohen_kappa_score,
    confusion_matrix,
    f1_score,
    matthews_corrcoef,
    precision_score,
    recall_score,
    roc_auc_score,
)
from sklearn.model_selection import StratifiedKFold
from sklearn.preprocessing import StandardScaler, label_binarize

import torch
import torch.nn as nn
import torch.nn.functional as F
from torch.utils.data import DataLoader, TensorDataset

warnings.filterwarnings("ignore")

# ─── hyper-parameters ─────────────────────────────────────────────────────────

N_FRAMES   = 64
N_CHANNELS = 14     # left pos(3) + left rot(4) + right pos(3) + right rot(4)
N_FEATURES = N_FRAMES * N_CHANNELS  # 896

EPOCHS       = 250
LR           = 3e-4
BATCH        = 32
WEIGHT_DECAY = 1e-4
PATIENCE     = 30   # early stopping

N_FOLDS = 5

# ─── column definitions ───────────────────────────────────────────────────────

FEATURE_COLS = [
    "left_pos_x",  "left_pos_y",  "left_pos_z",
    "left_rot_x",  "left_rot_y",  "left_rot_z",  "left_rot_w",
    "right_pos_x", "right_pos_y", "right_pos_z",
    "right_rot_x", "right_rot_y", "right_rot_z", "right_rot_w",
]

LEFT_POS_IDX  = [0, 1, 2]
RIGHT_POS_IDX = [7, 8, 9]

DATA_DIR   = Path(__file__).parent / "data" / "recordings"
MODELS_DIR = Path(__file__).parent / "models"
DEVICE     = torch.device("cpu")


# ─── feature extraction ───────────────────────────────────────────────────────

def _load_csv(path: Path) -> np.ndarray:
    return pd.read_csv(path)[FEATURE_COLS].to_numpy(dtype=np.float32)


def _resample(seq: np.ndarray, n: int) -> np.ndarray:
    t_in  = np.linspace(0.0, 1.0, len(seq))
    t_out = np.linspace(0.0, 1.0, n)
    out   = np.empty((n, seq.shape[1]), dtype=np.float32)
    for c in range(seq.shape[1]):
        out[:, c] = interp1d(t_in, seq[:, c])(t_out)
    return out


def extract_features(seq: np.ndarray) -> np.ndarray:
    """(T, 14) → (896,)  with translation normalisation."""
    seq = _resample(seq, N_FRAMES)
    seq[:, LEFT_POS_IDX]  -= seq[0, LEFT_POS_IDX]
    seq[:, RIGHT_POS_IDX] -= seq[0, RIGHT_POS_IDX]
    return seq.flatten()


def load_dataset():
    X, y = [], []
    label_map: dict[int, str] = {}

    for spell_dir in sorted(DATA_DIR.iterdir()):
        if not spell_dir.is_dir():
            continue
        idx_str, *parts = spell_dir.name.split("_")
        lid = int(idx_str) - 1
        label_map[lid] = "_".join(parts)

        files = sorted(spell_dir.glob("*.csv"))
        loaded = 0
        for p in files:
            try:
                seq = _load_csv(p)
                if len(seq) < 4:
                    continue
                X.append(extract_features(seq))
                y.append(lid)
                loaded += 1
            except Exception as exc:
                print(f"  [skip] {p.name}: {exc}")
        print(f"  {label_map[lid]:25s}  {loaded:3d} samples")

    return np.array(X, dtype=np.float32), np.array(y, dtype=np.int64), label_map


# ─── model ────────────────────────────────────────────────────────────────────

class SpellCNN(nn.Module):
    def __init__(self, n_classes: int):
        super().__init__()
        self.encoder = nn.Sequential(
            nn.Conv1d(N_CHANNELS, 32, kernel_size=5, padding=2),
            nn.ReLU(inplace=True),
            nn.MaxPool1d(2),

            nn.Conv1d(32, 64, kernel_size=3, padding=1),
            nn.ReLU(inplace=True),
            nn.MaxPool1d(2),

            nn.Conv1d(64, 128, kernel_size=3, padding=1),
            nn.ReLU(inplace=True),
            nn.AdaptiveAvgPool1d(1),
        )

        self.classifier = nn.Sequential(
            nn.Flatten(),
            nn.Dropout(0.4),
            nn.Linear(128, 64),
            nn.ReLU(inplace=True),
            nn.Dropout(0.3),
            nn.Linear(64, n_classes),
        )

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        return self.classifier(self.encoder(x))


class _ExportWrapper(nn.Module):
    def __init__(self, model: SpellCNN):
        super().__init__()
        self.model = model

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        return F.softmax(self.model(x), dim=1)


# ─── training helpers ─────────────────────────────────────────────────────────

def _make_loader(X: np.ndarray, y: np.ndarray, shuffle: bool) -> DataLoader:
    ds = TensorDataset(torch.tensor(X), torch.tensor(y))
    return DataLoader(ds, batch_size=BATCH, shuffle=shuffle)


def _build_model(n_classes: int) -> SpellCNN:
    return SpellCNN(n_classes).to(DEVICE)


def train_one_model(
    X_tr: np.ndarray,
    y_tr: np.ndarray,
    X_val: np.ndarray,
    y_val: np.ndarray,
    n_classes: int,
    verbose: bool = False,
) -> SpellCNN:
    model = _build_model(n_classes)
    opt   = torch.optim.Adam(model.parameters(), lr=LR, weight_decay=WEIGHT_DECAY)
    sched = torch.optim.lr_scheduler.CosineAnnealingLR(opt, T_max=EPOCHS)
    crit  = nn.CrossEntropyLoss()

    tr_loader  = _make_loader(X_tr, y_tr, shuffle=True)
    val_loader = _make_loader(X_val, y_val, shuffle=False)

    best_val_loss  = float("inf")
    best_weights   = None
    patience_count = 0

    for epoch in range(1, EPOCHS + 1):
        model.train()
        for xb, yb in tr_loader:
            opt.zero_grad()
            xb_reshaped = xb.to(DEVICE).view(-1, N_CHANNELS, N_FRAMES)
            crit(model(xb_reshaped), yb.to(DEVICE)).backward()
            opt.step()
        sched.step()

        model.eval()
        val_loss = 0.0
        with torch.no_grad():
            for xb, yb in val_loader:
                xb_reshaped = xb.to(DEVICE).view(-1, N_CHANNELS, N_FRAMES)
                val_loss += crit(model(xb_reshaped), yb.to(DEVICE)).item() * len(xb)
        val_loss /= len(X_val)

        if val_loss < best_val_loss - 1e-5:
            best_val_loss  = val_loss
            best_weights   = {k: v.cpu().clone() for k, v in model.state_dict().items()}
            patience_count = 0
        else:
            patience_count += 1
            if patience_count >= PATIENCE:
                if verbose:
                    print(f"    early stop at epoch {epoch:4d}  (best val loss {best_val_loss:.4f})")
                break

    model.load_state_dict(best_weights)
    return model


@torch.no_grad()
def predict(model: SpellCNN, X: np.ndarray):
    model.eval()
    x_tensor = torch.tensor(X).to(DEVICE).view(-1, N_CHANNELS, N_FRAMES)
    logits = model(x_tensor)
    proba  = F.softmax(logits, dim=1).cpu().numpy()
    return np.argmax(proba, axis=1), proba


# ─── metrics ──────────────────────────────────────────────────────────────────

def all_metrics(y_true, y_pred, y_proba, n_classes) -> dict:
    y_bin = label_binarize(y_true, classes=list(range(n_classes)))
    return {
        "accuracy":    accuracy_score(y_true, y_pred),
        "precision":   precision_score(y_true, y_pred, average="macro", zero_division=0),
        "recall":      recall_score(y_true, y_pred, average="macro", zero_division=0),
        "f1_macro":    f1_score(y_true, y_pred, average="macro", zero_division=0),
        "f1_weighted": f1_score(y_true, y_pred, average="weighted", zero_division=0),
        "kappa":       cohen_kappa_score(y_true, y_pred),
        "mcc":         matthews_corrcoef(y_true, y_pred),
        "roc_auc":     roc_auc_score(y_bin, y_proba, multi_class="ovr", average="macro"),
    }


def print_metrics(m: dict, title: str = "") -> None:
    if title:
        print(f"\n{title}")
    w = 16
    print(f"  {'Accuracy':<{w}}: {m['accuracy']:.4f}")
    print(f"  {'Macro F1':<{w}}: {m['f1_macro']:.4f}")
    print(f"  {'Macro Precision':<{w}}: {m['precision']:.4f}")
    print(f"  {'Macro Recall':<{w}}: {m['recall']:.4f}")
    print(f"  {'Weighted F1':<{w}}: {m['f1_weighted']:.4f}")
    print(f"  {'Cohen Kappa':<{w}}: {m['kappa']:.4f}")
    print(f"  {'MCC':<{w}}: {m['mcc']:.4f}")
    print(f"  {'ROC-AUC (OvR)':<{w}}: {m['roc_auc']:.4f}")


# ─── main ─────────────────────────────────────────────────────────────────────

def main():
    MODELS_DIR.mkdir(exist_ok=True)

    # ── load data ─────────────────────────────────────────────────────────────
    print("Loading recordings …\n")
    X, y, label_map = load_dataset()
    n_classes   = len(label_map)
    spell_names = [label_map[i] for i in range(n_classes)]

    print(f"\nDataset : {len(X)} samples  ×  {N_FEATURES} features  |  {n_classes} classes")
    print(f"Device  : {DEVICE}\n")

    # ── 5-fold cross-validation ───────────────────────────────────────────────
    sep = "─" * 62
    print(sep)
    print(f" 5-fold Stratified Cross-Validation")
    print(sep)

    cv = StratifiedKFold(n_splits=N_FOLDS, shuffle=True, random_state=42)

    all_true  = []
    all_pred  = []
    all_proba = []
    fold_accs = []

    for fold, (tr_idx, val_idx) in enumerate(cv.split(X, y), start=1):
        X_tr, X_val = X[tr_idx], X[val_idx]
        y_tr, y_val = y[tr_idx], y[val_idx]

        model = train_one_model(X_tr, y_tr, X_val, y_val, n_classes)
        preds, proba = predict(model, X_val)

        acc = accuracy_score(y_val, preds)
        fold_accs.append(acc)
        wrong = int((y_val != preds).sum())
        print(f"  Fold {fold}  val accuracy = {acc:.4f}   ({wrong} misclassified / {len(y_val)})")

        all_true.extend(y_val.tolist())
        all_pred.extend(preds.tolist())
        all_proba.append(proba)

    all_true  = np.array(all_true,  dtype=np.int64)
    all_pred  = np.array(all_pred,  dtype=np.int64)
    all_proba = np.vstack(all_proba)

    print(f"\n  Fold accuracy:  {np.mean(fold_accs):.4f} ± {np.std(fold_accs):.4f}")

    cv_metrics = all_metrics(all_true, all_pred, all_proba, n_classes)
    print_metrics(cv_metrics, title="Aggregate CV metrics (all held-out predictions combined):")

    print("\nPer-class report — cross-validation:\n")
    print(classification_report(all_true, all_pred, target_names=spell_names, digits=4))

    cm = confusion_matrix(all_true, all_pred)
    col_w = max(len(n) for n in spell_names) + 2
    print("Confusion matrix  (rows = true class, columns = predicted class):\n")
    print(" " * col_w + "".join(f"{n:>{col_w}}" for n in spell_names))
    for i, row in enumerate(cm):
        print(f"{spell_names[i]:<{col_w}}" + "".join(f"{v:>{col_w}}" for v in row))

    # ── final model on all data ───────────────────────────────────────────────
    print(f"\n{sep}")
    print(" Training final model on full dataset …")
    print(sep)

    final_model = train_one_model(X, y, X, y, n_classes, verbose=True)

    train_preds, train_proba = predict(final_model, X)
    train_metrics = all_metrics(y, train_preds, train_proba, n_classes)
    print_metrics(train_metrics, title="Training-set metrics (sanity check — not a generalisation estimate):")

    # ── save weights ──────────────────────────────────────────────────────────
    pt_path = MODELS_DIR / "spell_classifier.pt"
    torch.save(final_model.state_dict(), pt_path)
    print(f"\nSaved PyTorch weights  →  {pt_path}")

    # ── ONNX export (with softmax baked in) ───────────────────────────────────
    print("Exporting to ONNX …")
    final_model.eval()
    export_model = _ExportWrapper(final_model).eval()
    
    dummy_input = torch.zeros(1, N_CHANNELS, N_FRAMES, dtype=torch.float32)

    onnx_path = MODELS_DIR / "spell_classifier.onnx"
    
    torch.onnx.export(
        export_model,
        dummy_input,
        str(onnx_path),
        input_names=["gesture_input"],
        output_names=["probabilities"],
        opset_version=9,
        do_constant_folding=True,
        export_params=True
    )
    print(f"Saved ONNX model       →  {onnx_path}")

    # ── quick ONNX sanity check ───────────────────────────────────────────────
    try:
        import onnxruntime as ort
        sess  = ort.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
        # Ponieważ model ma stały Batch Size = 1, testujemy tylko jedną próbkę
        X_reshaped = X[0:1].reshape(1, N_CHANNELS, N_FRAMES)
        out   = sess.run(None, {"gesture_input": X_reshaped})[0]
        onnx_pred = np.argmax(out, axis=1)[0]
        print(f"ONNX check (1 sample): predicted={onnx_pred}  expected={y[0]}")
    except ImportError:
        print("(onnxruntime not installed — skipping ONNX inference check)")

    # ── metadata ──────────────────────────────────────────────────────────────
    labels_path = MODELS_DIR / "labels.json"
    with open(labels_path, "w") as f:
        json.dump({str(k): v for k, v in label_map.items()}, f, indent=2)
    print(f"Saved label map        →  {labels_path}")

    config = {
        "n_frames":          N_FRAMES,
        "n_channels":        N_CHANNELS,
        "n_features":        N_FEATURES,
        "feature_cols":      FEATURE_COLS,
        "left_pos_indices":  LEFT_POS_IDX,
        "right_pos_indices": RIGHT_POS_IDX,
        "onnx_input_name":   "gesture_input",
        "onnx_output_name":  "probabilities",
        "labels":            {str(k): v for k, v in label_map.items()},
        "preprocessing_note": (
            "1. Collect gesture frames (length >= 4). "
            "2. Linearly resample to 64 frames. "
            "3. Subtract first-frame position: left hand channels [0,1,2], right hand [7,8,9]. "
            "4. Feed directly to ONNX as a 3D tensor [1, 14, 64]. "
            "5. Output float[6] probabilities; argmax -> spell index."
        ),
    }
    config_path = MODELS_DIR / "feature_config.json"
    with open(config_path, "w") as f:
        json.dump(config, f, indent=2)
    print(f"Saved feature config   →  {config_path}")

    print("\n✓  All done.\n")


if __name__ == "__main__":
    main()