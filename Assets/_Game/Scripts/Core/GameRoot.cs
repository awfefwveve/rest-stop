using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace RestStop
{
    /// <summary>
    /// 씬에 하나만 있는 진입점. 데이터베이스와 네트워크 프리팹 목록을 들고 있고,
    /// 호스트/참가 메뉴를 띄운다. 네트워크 오브젝트는 전부 호스트가 런타임에 스폰한다.
    /// </summary>
    public class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance { get; private set; }

        public const int MaxPlayers = 4;

        [Header("데이터")]
        public ProductDatabase products;
        public CustomerDatabase customers;
        public EquipmentData[] equipment;

        [Header("네트워크 프리팹 (플레이어 제외 전부)")]
        public List<NetworkObject> networkPrefabs = new();
        public NetworkObject managersPrefab;
        public ProductBox boxPrefab;
        public TrashBag trashBagPrefab;
        public Mess messPrefab;

        [Header("씬")]
        public LevelLayout layout;
        public Camera menuCamera;

        string _ip = "127.0.0.1";
        string _port = "7777";
        bool _started;

        void Awake()
        {
            Instance = this;
            Loc.Init();
        }

        void Start()
        {
            var nm = NetworkManager.Singleton;
            foreach (var p in networkPrefabs)
            {
                if (p == null) continue;
                if (nm.NetworkConfig.Prefabs.Contains(p.gameObject)) continue;
                nm.AddNetworkPrefab(p.gameObject);
            }
            nm.NetworkConfig.ConnectionApproval = true;
            nm.ConnectionApprovalCallback = Approve;
            nm.OnClientDisconnectCallback += OnClientDisconnect;
        }

        void Approve(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse res)
        {
            var nm = NetworkManager.Singleton;
            bool full = nm.ConnectedClientsIds.Count >= MaxPlayers;
            bool midShift = DayCycle.Instance != null && DayCycle.Instance.Phase.Value == DayPhase.Open;
            res.Approved = !full && !midShift;
            res.CreatePlayerObject = res.Approved;
            res.Reason = full ? "full" : midShift ? "mid_shift" : "";

            // 처음부터 스폰 지점에 생성 (다른 사람 화면에서 원점에서 미끄러져 오지 않게)
            var spawn = LevelLayout.Instance ? LevelLayout.Instance.playerSpawn : null;
            if (res.Approved && spawn)
            {
                res.Position = spawn.position + spawn.right * (req.ClientNetworkId % 4) * 1.2f;
                res.Rotation = spawn.rotation;
            }
        }

        void OnClientDisconnect(ulong id)
        {
            var nm = NetworkManager.Singleton;
            if (!nm.IsServer && id == nm.LocalClientId)
            {
                _started = false;
                if (menuCamera) menuCamera.gameObject.SetActive(true);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        void SetTransport()
        {
            var t = NetworkManager.Singleton.GetComponent<UnityTransport>();
            ushort.TryParse(_port, out var port);
            if (port == 0) port = 7777;
            t.SetConnectionData(_ip, port, "0.0.0.0");
        }

        public void StartHost()
        {
            SetTransport();
            if (!NetworkManager.Singleton.StartHost()) return;
            _started = true;
            if (menuCamera) menuCamera.gameObject.SetActive(false);

            // 매니저와 매장 설비 스폰
            Instantiate(managersPrefab).Spawn();
            layout.SpawnAll();
            if (SaveSystem.Exists) SaveSystem.Load();
            else DeliveryService.Instance.ServerGrantStarter();
            DayCycle.Instance.ServerBeginPrep();
        }

        public void StartClient()
        {
            SetTransport();
            if (!NetworkManager.Singleton.StartClient()) return;
            _started = true;
            if (menuCamera) menuCamera.gameObject.SetActive(false);
        }

        void OnGUI()
        {
            if (_started) return;
            GUI.skin.label.fontSize = 18;
            GUI.skin.button.fontSize = 18;
            GUI.skin.textField.fontSize = 18;

            GUILayout.BeginArea(new Rect(40, 40, 420, 420), GUI.skin.box);
            GUILayout.Label(Loc.Get("menu_title"));
            GUILayout.Space(10);
            if (GUILayout.Button(Loc.Get(SaveSystem.Exists ? "menu_continue" : "menu_host"), GUILayout.Height(44))) StartHost();
            if (SaveSystem.Exists && GUILayout.Button(Loc.Get("menu_new_game"))) { SaveSystem.Delete(); StartHost(); }
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label("IP", GUILayout.Width(40));
            _ip = GUILayout.TextField(_ip);
            _port = GUILayout.TextField(_port, GUILayout.Width(80));
            GUILayout.EndHorizontal();
            if (GUILayout.Button(Loc.Get("menu_join"), GUILayout.Height(44))) StartClient();
            GUILayout.Space(16);
            if (GUILayout.Button(Loc.Get("menu_language") + ": " + Loc.CurrentName)) Loc.Next();
            GUILayout.EndArea();
        }
    }
}
