using UnityEngine;
using CardChat.UI;

/// <summary>
/// Sample driver that cycles a demo UI between two panel states so the <see cref="MenuStateScanner"/>
/// output can be observed in the console and compared against what the server receives.
/// Press Space (or wait <see cref="autoToggleSeconds"/> in auto mode) to swap panels; each swap forces
/// a scan and logs the resulting menustate text tree.
/// </summary>
public class MenuStateDemoController : MonoBehaviour
{
    [Tooltip("Scanner whose output is logged on each state change.")]
    public MenuStateScanner scanner;

    [Tooltip("First panel (active by default).")]
    public GameObject mainMenuPanel;

    [Tooltip("Second panel (inactive by default).")]
    public GameObject settingsPanel;

    [Tooltip("When > 0, panels auto-toggle on this interval (seconds) in addition to the Space key.")]
    public float autoToggleSeconds = 3f;

    private float _timer;
    private bool _showingSettings;

    private void Start()
    {
        ApplyState();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Toggle();
            return;
        }

        if (autoToggleSeconds > 0f)
        {
            _timer += Time.deltaTime;
            if (_timer >= autoToggleSeconds)
            {
                _timer = 0f;
                Toggle();
            }
        }
    }

    private void Toggle()
    {
        _showingSettings = !_showingSettings;
        ApplyState();
    }

    private void ApplyState()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(!_showingSettings);
        if (settingsPanel != null) settingsPanel.SetActive(_showingSettings);

        if (scanner != null)
        {
            scanner.ForceScan();
            Debug.Log("[MenuStateDemo] State -> " + (_showingSettings ? "Settings" : "MainMenu") + "\n" + scanner.BuildTextTree());
        }
    }
}
