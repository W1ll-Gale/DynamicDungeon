using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TileMapGeneratorDemoUI : MonoBehaviour
{
    [SerializeField] TMP_InputField widthInputField;
    [SerializeField] TMP_InputField heightInputField;
    [SerializeField] Toggle fillToScreenSize;
    [SerializeField] Button generateButton;
    [SerializeField] Button clearButton;
    [SerializeField] Slider randomFillPercentageSlider;
    [SerializeField] TextMeshProUGUI randomFillPercentageValueText;
    [SerializeField] Slider smoothingIterationsSlider;
    [SerializeField] TextMeshProUGUI smoothingIterationsValueText;
    [SerializeField] Toggle useBorderWallsToggle;
    [SerializeField] TMP_InputField seedInputField;
    [SerializeField] Toggle useRandomSeedToggle;
    [SerializeField] Slider mapZoomSlider;
    [SerializeField] Button resetCameraButton;
    [SerializeField] Camera mapCamera;
    [SerializeField] TilemapGenerator tileMapGenerator;

    void Start()
    {
        randomFillPercentageSlider.value = (float)tileMapGenerator.randomFillPercent / 100f;
        randomFillPercentageValueText.text = $"Random Fill {tileMapGenerator.randomFillPercent}%";
        smoothingIterationsSlider.value = (float)tileMapGenerator.smoothIterations / 10f;
        smoothingIterationsValueText.text = $"Smoothing Iterations {tileMapGenerator.smoothIterations}";
        fillToScreenSize.isOn = false;
        useBorderWallsToggle.isOn = tileMapGenerator.useBorderWalls;
        seedInputField.text = tileMapGenerator.seed;
        useRandomSeedToggle.isOn = tileMapGenerator.useRandomSeed;
        seedInputField.interactable = !tileMapGenerator.useRandomSeed;
        widthInputField.text = tileMapGenerator.width.ToString();
        heightInputField.text = tileMapGenerator.height.ToString();
        mapZoomSlider.value = (mapCamera.orthographicSize - 5f) / (50f - 5f);

        void UpdateMapSizeToCamera()
        {
            float camHeight = mapCamera.orthographicSize * 2f;
            float camWidth = camHeight * mapCamera.aspect;

            int screenWidthInTiles = Mathf.CeilToInt(camWidth);
            int screenHeightInTiles = Mathf.CeilToInt(camHeight);

            tileMapGenerator.width = screenWidthInTiles;
            tileMapGenerator.height = screenHeightInTiles;
            widthInputField.text = screenWidthInTiles.ToString();
            heightInputField.text = screenHeightInTiles.ToString();
        }

        fillToScreenSize.onValueChanged.AddListener((isOn) =>
        {
            if (isOn)
            {
                UpdateMapSizeToCamera();
                widthInputField.interactable = false;
                heightInputField.interactable = false;
            }
            else
            {
                widthInputField.interactable = true;
                heightInputField.interactable = true;
            }
        });

        widthInputField.onValueChanged.AddListener((value) =>
        {
            if (int.TryParse(value, out int width))
            {
                tileMapGenerator.width = Mathf.Max(1, width);
            }
        });

        heightInputField.onValueChanged.AddListener((value) =>
        {
            if (int.TryParse(value, out int height))
            {
                tileMapGenerator.height = Mathf.Max(1, height);
            }
        });

        mapZoomSlider.onValueChanged.AddListener((value) =>
        {
            mapCamera.orthographicSize = Mathf.Lerp(5f, 50f, value);
            if (fillToScreenSize.isOn)
            {
                UpdateMapSizeToCamera();
            }
        });

        resetCameraButton.onClick.AddListener(() =>
        {
            mapCamera.orthographicSize = 5f;
            mapZoomSlider.value = (5f - 5f) / (50f - 5f);
        });

        seedInputField.onValueChanged.AddListener((value) =>
        {
            tileMapGenerator.seed = value;
        });

        useRandomSeedToggle.onValueChanged.AddListener((isOn) =>
        {
            tileMapGenerator.useRandomSeed = isOn;
            seedInputField.interactable = !isOn;
        });

        randomFillPercentageSlider.onValueChanged.AddListener((value) =>
        {
            tileMapGenerator.randomFillPercent = (int)(value*100);
            randomFillPercentageValueText.text = $"Random Fill {tileMapGenerator.randomFillPercent}%";
        });

        smoothingIterationsSlider.onValueChanged.AddListener((value) =>
        {
            tileMapGenerator.smoothIterations = (int)(value * 10);
            smoothingIterationsValueText.text = $"Smoothing Iterations {tileMapGenerator.smoothIterations}";
        });

        generateButton.onClick.AddListener(() =>
        {
            tileMapGenerator.randomFillPercent = (int)(randomFillPercentageSlider.value*100);
            tileMapGenerator.smoothIterations = (int)(smoothingIterationsSlider.value * 10);
            tileMapGenerator.useBorderWalls = useBorderWallsToggle.isOn;
            tileMapGenerator.useRandomSeed = useRandomSeedToggle.isOn;
            tileMapGenerator.seed = seedInputField.text;
            tileMapGenerator.GenerateTilemap();
            seedInputField.text = tileMapGenerator.seed;
        });

        clearButton.onClick.AddListener(() =>
        {
            tileMapGenerator.ClearGeneratedMap();
        });
    }

    void Update()
    {
        
    }
}
