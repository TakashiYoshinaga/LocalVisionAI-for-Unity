using UnityEngine;

public class MainCoordinator : MonoBehaviour
{
    [SerializeField] private ImageCaptureManager _imageCaptureManager;
    [SerializeField] private VisionAiManager _visionAiManager;
    [SerializeField] private ShowResultManager _showResultManager;
    [SerializeField] private TMPro.TMP_Text _resultText;

    private VisionAiDataSource _visionAiDataSource = new VisionAiDataSource();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
