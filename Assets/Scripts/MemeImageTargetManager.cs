using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Vuforia;

public class MemeImageTargetManager : MonoBehaviour
{
    [Header("Menu Camera (used while menu is visible)")]
    public Camera menuCamera;

    [Header("Vuforia / Camera")]
    public ObserverBehaviour imageTargetObserver;      // the image target to listen to (assign in inspector)
    public GameObject ARCameraRoot;                   // ARCamera GameObject to enable/disable

    [Header("Prefab")]
    public GameObject memePrefab;

    [Header("UI - Menu")]
    public GameObject menuPanel;                      // Panel that covers screen at start (menu)
    public UnityEngine.UI.Image menuBackgroundImage;  // optional UI Image inside menuPanel to show background image
    public Button menuScanButton;                     // button in menu to start scanning / enable camera
    public Button menuCloseButton;                    // CLOSE button in menu -> quit app

    [Header("UI - In-AR")]
    public Button updateButton;                       // visible only while target is tracked
    public Button exitToMenuButton;                   // visible while camera is active (even if target lost)

    [Header("GitHub Base Link (RAW!)")]
    public string baseLink = "https://raw.githubusercontent.com/Priyanshu-scieck/meme_images/main/meme";
    public string format = ".jpg";
    [Header("Sequence Settings")]
    public int minIndex = 1;
    public int maxIndex = 24;

    // Optional: local background file (Editor testing). Your uploaded file path:
    // file:///mnt/data/1000026540.jpg
    [Header("Editor local menu background (optional)")]
    public bool useLocalMenuBg = true;
    public string localMenuBgPath = "file:///mnt/data/1000026540.jpg";

    // runtime
    private GameObject currentPlaced = null;
    private MeshRenderer currentQuadRenderer = null;
    private int currentIndex = 1;
    private bool isLoading = false;
    private bool cameraActive = false;
    private bool subscribedToTarget = false;

    void Awake()
    {
        if (menuCamera != null) menuCamera.enabled = true; // ensure menu camera renders initially
        // make sure AR camera is off at start
        if (ARCameraRoot != null)
            ARCameraRoot.SetActive(false);

        // menu visible at start
        if (menuPanel != null)
            menuPanel.SetActive(true);

        // in-AR UI hidden initially
        if (updateButton != null) updateButton.gameObject.SetActive(false);
        if (exitToMenuButton != null) exitToMenuButton.gameObject.SetActive(false);

        // wire menu buttons
        if (menuScanButton != null) menuScanButton.onClick.AddListener(OnMenuScanPressed);
        if (menuCloseButton != null) menuCloseButton.onClick.AddListener(OnMenuClosePressed);

        // wire in-AR buttons
        if (updateButton != null) updateButton.onClick.AddListener(OnUpdateClicked);
        if (exitToMenuButton != null) exitToMenuButton.onClick.AddListener(OnExitToMenuClicked);

        // optionally load local menu background for editor quick testing
        if (useLocalMenuBg && menuBackgroundImage != null && !string.IsNullOrEmpty(localMenuBgPath))
        {
            StartCoroutine(LoadLocalMenuBackground(localMenuBgPath));
        }
    }

    void OnDestroy()
    {
        // remove listeners
        if (menuScanButton != null) menuScanButton.onClick.RemoveListener(OnMenuScanPressed);
        if (menuCloseButton != null) menuCloseButton.onClick.RemoveListener(OnMenuClosePressed);
        if (updateButton != null) updateButton.onClick.RemoveListener(OnUpdateClicked);
        if (exitToMenuButton != null) exitToMenuButton.onClick.RemoveListener(OnExitToMenuClicked);

        UnsubscribeFromImageTarget();
    }

    // ------------ Menu button callbacks ------------

    private void OnMenuScanPressed()
    {
        if (menuCamera != null) menuCamera.enabled = false;    // stop menu camera

        // enable AR camera and start listening to image target
        if (ARCameraRoot != null) ARCameraRoot.SetActive(true);
        cameraActive = true;

        // hide menu
        if (menuPanel != null) menuPanel.SetActive(false);

        // ensure exit button visible while camera active
        if (exitToMenuButton != null)
        {
            exitToMenuButton.gameObject.SetActive(true);
            exitToMenuButton.interactable = true;
        }

        // subscribe to target detection now
        SubscribeToImageTarget();
    }

    private void OnMenuClosePressed()
    {
        // Close the application
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------ In-AR button callbacks ------------

    private void OnExitToMenuClicked()
    {
        // Stop AR and go back to menu
        // destroy placed content to ensure a clean state
        if (currentPlaced != null)
        {
            Destroy(currentPlaced);
            currentPlaced = null;
            currentQuadRenderer = null;
        }

        // stop listening to target
        UnsubscribeFromImageTarget();

        // disable AR camera
        if (ARCameraRoot != null) ARCameraRoot.SetActive(false);
        cameraActive = false;

        // hide in-AR UI
        if (updateButton != null) updateButton.gameObject.SetActive(false);
        if (exitToMenuButton != null) exitToMenuButton.gameObject.SetActive(false);

        // show menu again
        if (menuPanel != null) menuPanel.SetActive(true);

        if (menuCamera != null) menuCamera.enabled = true; // bring menu camera back
    }

    private void OnUpdateClicked()
    {
        if (isLoading || currentQuadRenderer == null) return;

        currentIndex++;
        if (currentIndex > maxIndex) currentIndex = minIndex;

        StartCoroutine(LoadMeme(currentIndex));
    }

    // ------------ Image target subscription ------------

    private void SubscribeToImageTarget()
    {
        if (imageTargetObserver != null && !subscribedToTarget)
        {
            imageTargetObserver.OnTargetStatusChanged += OnTargetStatusChanged;
            subscribedToTarget = true;
        }
    }

    private void UnsubscribeFromImageTarget()
    {
        if (imageTargetObserver != null && subscribedToTarget)
        {
            imageTargetObserver.OnTargetStatusChanged -= OnTargetStatusChanged;
            subscribedToTarget = false;
        }
    }

    private void OnTargetStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
    {
        Debug.Log($"[MemeManager] TargetStatusChanged: {behaviour?.gameObject.name} -> {status.Status} ({status.StatusInfo})");

        if (status.Status == Status.TRACKED || status.Status == Status.EXTENDED_TRACKED)
            OnTargetFound(behaviour);
        else
            OnTargetLost();
    }

    // ------------ Target found / lost ------------

    private void OnTargetFound(ObserverBehaviour behaviour)
    {
        Debug.Log("[MemeManager] OnTargetFound: " + (behaviour != null ? behaviour.gameObject.name : "null"));

        // instantiate meme prefab as child of the target (or reuse)
        if (currentPlaced == null)
        {
            currentPlaced = Instantiate(memePrefab, behaviour.transform);
            currentPlaced.transform.localPosition = Vector3.zero;
            currentPlaced.transform.localRotation = Quaternion.identity;
        }
        else
        {
            // re-parent in case it was detached
            currentPlaced.transform.SetParent(behaviour.transform, false);
            currentPlaced.SetActive(true);
        }

        currentQuadRenderer = currentPlaced.GetComponentInChildren<MeshRenderer>();

        // Ensure all children renderers and canvases are enabled
        var renderers = currentPlaced.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers) r.enabled = true;

        var canvases = currentPlaced.GetComponentsInChildren<Canvas>(true);
        foreach (var c in canvases) c.enabled = true;

        // show in-AR UI (visible while target tracked)
        if (updateButton != null) updateButton.gameObject.SetActive(true);
        if (exitToMenuButton != null) exitToMenuButton.gameObject.SetActive(true);

        // disable update button until first load completes
        if (updateButton != null) updateButton.interactable = false;

        // load the current index
        StartCoroutine(LoadMeme(currentIndex));
    }

    private void OnTargetLost()
    {
        Debug.Log("[MemeManager] OnTargetLost");

        if (currentPlaced != null)
        {
            // disable all renderers (mesh, sprite, particle renderers etc.)
            var renderers = currentPlaced.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers) r.enabled = false;

            // disable any canvas (UI) children
            var canvases = currentPlaced.GetComponentsInChildren<Canvas>(true);
            foreach (var c in canvases) c.enabled = false;

            // also set inactive to be safe
            currentPlaced.SetActive(false);
        }

        // hide update button (no updates possible)
        if (updateButton != null)
            updateButton.gameObject.SetActive(false);

        // keep exit button visible if camera is active so user can return to menu
        if (exitToMenuButton != null && cameraActive)
        {
            exitToMenuButton.gameObject.SetActive(true);
            exitToMenuButton.interactable = true;
        }
        else if (exitToMenuButton != null && !cameraActive)
        {
            exitToMenuButton.gameObject.SetActive(false);
        }
    }

    // ------------ Load meme coroutine (same as before, with minor UI toggles) ------------

    private IEnumerator LoadMeme(int index)
    {
        if (currentQuadRenderer == null)
            yield break;

        isLoading = true;
        if (updateButton != null) updateButton.interactable = false;

        string url = baseLink + index + format;

        using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(url))
        {
            uwr.timeout = 15;
            yield return uwr.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (uwr.result != UnityWebRequest.Result.Success)
#else
            if (uwr.isHttpError || uwr.isNetworkError)
#endif
            {
                Debug.LogError("Error loading meme " + index + " : " + uwr.error);
                // allow retry
                if (updateButton != null) updateButton.interactable = true;
                isLoading = false;
                yield break;
            }

            Texture2D tex = DownloadHandlerTexture.GetContent(uwr);
            if (tex == null)
            {
                Debug.LogError("Downloaded texture was null for index " + index);
                if (updateButton != null) updateButton.interactable = true;
                isLoading = false;
                yield break;
            }

            currentQuadRenderer.material.mainTexture = tex;
            AdjustQuadScale(currentQuadRenderer.transform, tex);
        }

        isLoading = false;
        if (updateButton != null)
            updateButton.interactable = true;
    }

    private void AdjustQuadScale(Transform quad, Texture2D tex)
    {
        if (quad == null || tex == null) return;

        float aspect = (float)tex.width / tex.height;
        float baseSize = 0.3f;

        quad.localScale = new Vector3(baseSize * aspect, baseSize, 1);
        quad.localRotation = Quaternion.Euler(90, 0, 0);
    }

    // ------------ optional helper: load menu bg from local path (Editor) ------------

    private IEnumerator LoadLocalMenuBackground(string localFileUrl)
    {
        using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(localFileUrl))
        {
            yield return uwr.SendWebRequest();
#if UNITY_2020_1_OR_NEWER
            if (uwr.result != UnityWebRequest.Result.Success)
#else
            if (uwr.isHttpError || uwr.isNetworkError)
#endif
            {
                Debug.LogWarning("Local menu bg load failed: " + uwr.error);
                yield break;
            }

            Texture2D tex = DownloadHandlerTexture.GetContent(uwr);
            if (tex == null) yield break;

            // create sprite and assign
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            if (menuBackgroundImage != null) menuBackgroundImage.sprite = sprite;
        }
    }
}