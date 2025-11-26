using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TileMapGeneratorDemoUI : MonoBehaviour
{

    [SerializeField] Button generateButton;
    [SerializeField] Button clearButton;
    [SerializeField] Slider randomFillPercentageSlider;
    [SerializeField] TextMeshProUGUI randomFillPercentageValueText;
    [SerializeField] Toggle useBorderWallsToggle;
    [SerializeField] TMP_InputField seedInputField;
    [SerializeField] Toggle useRandomSeedToggle;
    [SerializeField] TilemapGenerator tileMapGenerator;

    void Start()
    {
        randomFillPercentageSlider.value = tileMapGenerator.randomFillPercent/100;
        randomFillPercentageValueText.text = $"Random Fill {tileMapGenerator.randomFillPercent}%";
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

        generateButton.onClick.AddListener(() =>
        {
            tileMapGenerator.randomFillPercent = (int)(randomFillPercentageSlider.value*100);
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
