using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TileMapGeneratorDemoUI : MonoBehaviour
{

    [SerializeField] Button generateButton;
    [SerializeField] Button clearButton;
    [SerializeField] Slider randomFillPercentageSlider;
    [SerializeField] TextMeshProUGUI randomFillPercentageValueText;
    [SerializeField] Slider smoothingIterationsSlider;
    [SerializeField] TextMeshProUGUI smoothingIterationsValueText;
    [SerializeField] Toggle useBorderWallsToggle;
    [SerializeField] TMP_InputField seedInputField;
    [SerializeField] Toggle useRandomSeedToggle;
    [SerializeField] TilemapGenerator tileMapGenerator;

    void Start()
    {
        randomFillPercentageSlider.value = (float)tileMapGenerator.randomFillPercent / 100f;
        randomFillPercentageValueText.text = $"Random Fill {tileMapGenerator.randomFillPercent}%";
        smoothingIterationsSlider.value = (float)tileMapGenerator.smoothIterations / 10f;
        smoothingIterationsValueText.text = $"Smoothing Iterations {tileMapGenerator.smoothIterations}";
        useBorderWallsToggle.isOn = tileMapGenerator.useBorderWalls;
        seedInputField.text = tileMapGenerator.seed;
        useRandomSeedToggle.isOn = tileMapGenerator.useRandomSeed;
        seedInputField.interactable = !tileMapGenerator.useRandomSeed;

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
