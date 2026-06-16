import onnx
from onnx.external_data_helper import load_external_data_for_model, convert_model_to_external_data
from pathlib import Path

# Ścieżki do Twoich plików
MODELS_DIR = Path(__file__).parent / "models"
onnx_path = MODELS_DIR / "spell_classifier.onnx"
output_single_path = MODELS_DIR / "spell_classifier_fixed.onnx"

print("Ładowanie modelu ONNX...")
# 1. Ładujemy główny plik ONNX bez automatycznego wczytywania danych zewnętrznych
model = onnx.load(str(onnx_path), load_external_data=False)

# 2. Ręcznie wskazujemy folder, w którym leży plik .data, aby uzupełnić wagi
load_external_data_for_model(model, str(MODELS_DIR))

# 3. Usuwamy z nagłówków informacje o pliku zewnętrznym, co zmusza do zapisu wewnątrz pliku
for initializer in model.graph.initializer:
    if initializer.HasField("data_location") and initializer.data_location == onnx.TensorProto.EXTERNAL:
        initializer.data_location = onnx.TensorProto.DEFAULT
        initializer.external_data.clear()

# 4. Zapisujemy jako pojedynczy, samodzielny plik
print("Zapisywanie scalonego modelu (Single File)...")
onnx.save_model(model, str(output_single_path))

print(f"Sukces! Nowy plik powstał w: {output_single_path}")
print("Możesz teraz przenieść TYLKO plik 'spell_classifier_fixed.onnx' do Unity.")