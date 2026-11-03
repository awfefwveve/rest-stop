using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace RestStop.EditorTools
{
    /// <summary>
    /// 메뉴 RestStop > 프로토타입 전체 생성 한 번으로
    /// 샘플 데이터, 그레이박스 프리팹, 휴게소 씬, 네비메시, 네트워크 매니저까지 만든다.
    /// 다시 실행하면 같은 경로의 에셋을 덮어쓴다 (데이터 값도 초기값으로 돌아감 주의).
    /// </summary>
    public static class RestStopBuilder
    {
        const string Root = "Assets/_Game";
        const string DataDir = Root + "/Data";
        const string PrefabDir = Root + "/Prefabs";
        const string MatDir = Root + "/Art/Materials";
        const string SceneDir = Root + "/Scenes";
        const string ScenePath = SceneDir + "/RestStop_Proto.unity";

        static readonly Dictionary<string, Material> _mats = new();

        class Db
        {
            public ProductDatabase products;
            public CustomerDatabase customers;
            public Dictionary<string, EquipmentData> equipment = new();
            public Dictionary<string, EventData> events = new();
        }

        class Prefabs
        {
            public GameObject player;
            public NetworkObject managers, box, trashBag, mess, shelfAmbient, shelfChilled, shelfFrozen;
            public NetworkObject counterShop, counterFood, terminal, trashBin, dumpster;
            public Dictionary<string, NetworkObject> stations = new();
            public List<NetworkObject> all = new();
        }

        [MenuItem("RestStop/프로토타입 전체 생성 (데이터 + 프리팹 + 씬)")]
        public static void BuildAll()
        {
            if (!EditorUtility.DisplayDialog("RestStop", "샘플 데이터, 프리팹, 씬을 생성(덮어쓰기)합니다. 계속할까요?", "생성", "취소")) return;
            Folders();
            var db = BuildData();
            var pf = BuildPrefabs(db);
            BuildCustomers(db);
            BuildScene(db, pf);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("RestStop", "완료. Play 후 '가게 열기(호스트)'를 누르세요.\n멀티 테스트: Window > Multiplayer > Multiplayer Play Mode", "확인");
        }

        // ------------------------------------------------------------------ 공통

        static void Folders()
        {
            foreach (var d in new[] { DataDir, DataDir + "/Products", DataDir + "/Customers", DataDir + "/Equipment", DataDir + "/Events", PrefabDir, PrefabDir + "/Customers", MatDir, SceneDir })
                Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
        }

        static T Asset<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static Material Mat(string name, Color c)
        {
            if (_mats.TryGetValue(name, out var cached) && cached) return cached;
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            EditorUtility.SetDirty(m);
            _mats[name] = m;
            return m;
        }

        static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 pos, Vector3 scale, Material m, bool collider = false)
        {
            var g = GameObject.CreatePrimitive(t);
            g.name = name;
            if (!collider) Object.DestroyImmediate(g.GetComponent<Collider>());
            if (parent) g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            if (m) g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        static GameObject Empty(string name, Transform parent, Vector3 pos, float yaw = 0f)
        {
            var g = new GameObject(name);
            if (parent) g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return g;
        }

        static T Save<T>(GameObject temp, string path) where T : Component
        {
            var asset = PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);
            return asset.GetComponent<T>();
        }

        static void Obstacle(GameObject g, Vector3 center, Vector3 size)
        {
            var o = g.AddComponent<NavMeshObstacle>();
            o.shape = NavMeshObstacleShape.Box;
            o.center = center;
            o.size = size;
            o.carving = true;
            o.carveOnlyStationary = true;
        }

        static void StaticTransform(NetworkTransform nt, bool yawOnly)
        {
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            if (yawOnly) { nt.SyncRotAngleX = false; nt.SyncRotAngleZ = false; }
        }

        // ------------------------------------------------------------------ 데이터

        static Db BuildData()
        {
            var db = new Db();

            // 상품 (가상 브랜드. 이름은 Loc 키)
            db.products = Asset<ProductDatabase>(DataDir + "/ProductDatabase.asset");
            db.products.items.Clear();
            void P(string id, ProductCategory cat, StorageType st, int cost, int price, int life, int unlock, Color c)
            {
                var p = Asset<ProductData>($"{DataDir}/Products/{id}.asset");
                p.id = id; p.nameKey = "prod_" + id; p.category = cat; p.storage = st;
                p.cost = cost; p.price = price; p.unitsPerBox = 12; p.maxPerSlot = 12;
                p.shelfLifeDays = life; p.unlockLevel = unlock; p.fallbackColor = c;
                EditorUtility.SetDirty(p);
                db.products.items.Add(p);
            }
            P("chips", ProductCategory.Snack, StorageType.Ambient, 900, 1700, 0, 1, new Color(1f, 0.55f, 0.1f));
            P("choco", ProductCategory.Snack, StorageType.Ambient, 800, 1500, 0, 1, new Color(0.45f, 0.25f, 0.1f));
            P("walnut_cake", ProductCategory.Snack, StorageType.Ambient, 1500, 3500, 2, 1, new Color(0.8f, 0.6f, 0.35f));
            P("cup_ramen", ProductCategory.Instant, StorageType.Ambient, 700, 1400, 0, 1, new Color(0.9f, 0.15f, 0.15f));
            P("cola", ProductCategory.Drink, StorageType.Chilled, 700, 1500, 0, 1, new Color(0.6f, 0.05f, 0.05f));
            P("canned_coffee", ProductCategory.Drink, StorageType.Chilled, 600, 1300, 0, 1, new Color(0.3f, 0.2f, 0.15f));
            P("banana_milk", ProductCategory.Drink, StorageType.Chilled, 800, 1700, 3, 1, new Color(1f, 0.9f, 0.3f));
            P("kimbap", ProductCategory.Chilled, StorageType.Chilled, 900, 1500, 1, 1, new Color(0.15f, 0.4f, 0.15f));
            P("icecream", ProductCategory.Frozen, StorageType.Frozen, 600, 1500, 0, 2, new Color(0.7f, 0.85f, 1f));
            P("tissue", ProductCategory.Goods, StorageType.Ambient, 1000, 2000, 0, 2, new Color(0.95f, 0.95f, 0.95f));
            EditorUtility.SetDirty(db.products);

            // 설비
            void E(string id, Zone z, float cook, float burn, int batch, int unitCost, int price, int care, int refill, int daily)
            {
                var e = Asset<EquipmentData>($"{DataDir}/Equipment/{id}.asset");
                e.id = id; e.nameKey = "eq_" + id; e.zone = z; e.cookSeconds = cook; e.burnSeconds = burn;
                e.batchSize = batch; e.unitCost = unitCost; e.price = price; e.careUses = care; e.refillCost = refill; e.dailyCost = daily;
                EditorUtility.SetDirty(e);
                db.equipment[id] = e;
            }
            E("odeng", Zone.Shop, 20f, 60f, 6, 300, 1000, 5, 6000, 5000);
            E("sweet_potato", Zone.Shop, 35f, 50f, 4, 500, 2500, 4, 8000, 4000);
            E("ramen", Zone.FoodCourt, 15f, 30f, 2, 1500, 5000, 6, 9000, 8000);
            E("udon", Zone.FoodCourt, 20f, 35f, 2, 1800, 6000, 6, 10000, 8000);

            // 이벤트
            void V(string id, GameEventType t, int mess, float limit, int sMoney, float sRep, int fMoney, float fRep)
            {
                var v = Asset<EventData>($"{DataDir}/Events/{id}.asset");
                v.id = id; v.titleKey = $"ev_{id}_title"; v.lineKey = $"ev_{id}_line"; v.type = t;
                v.messCount = mess; v.timeLimit = limit; v.successMoney = sMoney; v.successRep = sRep; v.failMoney = fMoney; v.failRep = fRep;
                EditorUtility.SetDirty(v);
                db.events[id] = v;
            }
            V("raccoon_trash", GameEventType.SpawnMess, 4, 50f, 0, 2f, 0, -5f);
            V("sleepy", GameEventType.BlockUntilPoked, 0, 30f, 0, 1f, 0, -3f);
            V("robot_battery", GameEventType.BlockUntilPoked, 0, 30f, 5000, 1f, 0, -2f);
            V("alien_coin", GameEventType.StrangeCoin, 0, 25f, 15000, 1f, -8000, -1f);
            V("ghost_spill", GameEventType.SpawnMess, 3, 40f, 10000, 2f, 0, -4f);
            V("traveler_coin", GameEventType.StrangeCoin, 0, 25f, 50000, 3f, -20000, -2f);
            V("dragon_sneeze", GameEventType.SpawnMess, 6, 60f, 30000, 4f, 0, -6f);

            db.customers = Asset<CustomerDatabase>(DataDir + "/CustomerDatabase.asset");
            return db;
        }

        static void BuildCustomers(Db db)
        {
            db.customers.items.Clear();
            void C(string id, Rarity r, float weight, Color color, float scale, float speed, ProductCategory[] likes, int min, int max,
                   float eq, float food, float patience, float mess, float evChance, string[] evs, string requires = "", float from = 0f, float to = 24f, float minRep = 0f)
            {
                var prefab = CustomerPrefab(id, color, scale, speed);
                var c = Asset<CustomerData>($"{DataDir}/Customers/{id}.asset");
                c.id = id; c.nameKey = "cust_" + id; c.rarity = r; c.weight = weight;
                c.likes = likes; c.itemsMin = min; c.itemsMax = max;
                c.equipmentOrderChance = eq; c.foodCourtChance = food; c.patience = patience; c.messiness = mess;
                c.eventChance = evChance; c.idleLineKey = $"cust_{id}_line";
                c.requiresEquipmentId = requires; c.hourFrom = from; c.hourTo = to; c.minReputation = minRep;
                var list = new List<EventData>();
                foreach (var e in evs) if (db.events.TryGetValue(e, out var ed)) list.Add(ed);
                c.events = list.ToArray();
                c.prefab = prefab;
                EditorUtility.SetDirty(c);
                db.customers.items.Add(c);
            }
            var S = ProductCategory.Snack; var D = ProductCategory.Drink; var I = ProductCategory.Instant;
            var Ch = ProductCategory.Chilled; var F = ProductCategory.Frozen; var G = ProductCategory.Goods;

            // 일반
            C("office", Rarity.Common, 1f, new Color(0.3f, 0.35f, 0.5f), 1f, 2.4f, new[] { D, S, Ch }, 1, 3, 0.3f, 0.35f, 50f, 0.05f, 0.1f, new[] { "sleepy" });
            C("student", Rarity.Common, 1f, new Color(0.2f, 0.5f, 0.3f), 0.9f, 2.6f, new[] { S, I, F }, 1, 4, 0.4f, 0.25f, 70f, 0.15f, 0.1f, new[] { "sleepy" });
            C("trucker", Rarity.Common, 0.9f, new Color(0.55f, 0.35f, 0.2f), 1.1f, 2.2f, new[] { D, I }, 1, 2, 0.3f, 0.6f, 40f, 0.1f, 0f, new string[0]);
            C("family", Rarity.Common, 0.7f, new Color(0.8f, 0.5f, 0.6f), 1f, 2f, new[] { S, D, Ch, F }, 3, 5, 0.5f, 0.5f, 60f, 0.25f, 0f, new string[0]);
            C("hiker", Rarity.Common, 0.8f, new Color(0.3f, 0.6f, 0.2f), 1f, 2.4f, new[] { D, Ch, G }, 2, 3, 0.2f, 0.3f, 80f, 0.05f, 0f, new string[0]);
            // 희귀
            C("raccoon", Rarity.Rare, 1f, new Color(0.45f, 0.45f, 0.45f), 0.5f, 3.2f, new[] { S }, 1, 2, 0.1f, 0f, 30f, 0.4f, 0.8f, new[] { "raccoon_trash" });
            C("robot", Rarity.Rare, 1f, new Color(0.7f, 0.75f, 0.8f), 0.85f, 1.8f, new[] { G, D }, 1, 3, 0f, 0f, 120f, 0f, 0.4f, new[] { "robot_battery" });
            C("grandma", Rarity.Rare, 0.8f, new Color(0.6f, 0.4f, 0.7f), 0.85f, 1.4f, new[] { S, Ch }, 2, 4, 0.6f, 0.4f, 200f, 0f, 0f, new string[0]);
            // 영웅
            C("alien", Rarity.Epic, 1f, new Color(0.3f, 1f, 0.4f), 1.05f, 2.4f, new[] { S, D }, 2, 4, 0.5f, 0.3f, 60f, 0.1f, 0.9f, new[] { "alien_coin" });
            C("ghost", Rarity.Epic, 1f, new Color(0.9f, 0.95f, 1f), 1f, 1.6f, new[] { Ch }, 1, 1, 1f, 0f, 90f, 0f, 0.7f, new[] { "ghost_spill" }, "odeng", 19f, 24f);
            // 전설
            C("time_traveler", Rarity.Legendary, 1f, new Color(1f, 0.8f, 0.2f), 1f, 2.6f, new[] { S }, 1, 2, 0.5f, 0.5f, 40f, 0f, 1f, new[] { "traveler_coin" });
            C("dragon", Rarity.Legendary, 0.8f, new Color(0.8f, 0.1f, 0.1f), 1.6f, 2f, new[] { I }, 3, 5, 1f, 0f, 30f, 0.5f, 0.6f, new[] { "dragon_sneeze" }, "sweet_potato", 0f, 24f, 30f);

            EditorUtility.SetDirty(db.customers);
        }

        static NetworkObject CustomerPrefab(string id, Color color, float scale, float speed)
        {
            var root = new GameObject("Customer_" + id);
            var no = root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            StaticTransform(nt, true);
            var col = root.AddComponent<CapsuleCollider>();
            col.height = 1.8f * scale; col.radius = 0.3f * scale; col.center = new Vector3(0f, 0.9f * scale, 0f);
            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.3f * scale; agent.height = 1.8f * scale; agent.speed = speed;
            agent.angularSpeed = 720f; agent.acceleration = 12f; agent.stoppingDistance = 0.2f;
            root.AddComponent<CustomerAI>();

            var m = Mat("Cust_" + id, color);
            Prim(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 0.9f * scale, 0f), new Vector3(0.6f, 0.9f, 0.6f) * scale, m);
            Prim(PrimitiveType.Cube, "Nose", root.transform, new Vector3(0f, 1.5f * scale, 0.3f * scale), new Vector3(0.12f, 0.12f, 0.2f) * scale, Mat("Dark", new Color(0.1f, 0.1f, 0.1f)));
            return Save<NetworkObject>(root, $"{PrefabDir}/Customers/Customer_{id}.prefab");
        }

        // ------------------------------------------------------------------ 프리팹

        static Prefabs BuildPrefabs(Db db)
        {
            var pf = new Prefabs();
            var wood = Mat("Wood", new Color(0.55f, 0.4f, 0.25f));
            var metal = Mat("Metal", new Color(0.6f, 0.62f, 0.65f));
            var dark = Mat("Dark", new Color(0.1f, 0.1f, 0.1f));
            var cardboard = Mat("Cardboard", new Color(0.7f, 0.55f, 0.35f));
            var white = Mat("White", new Color(0.9f, 0.9f, 0.9f));

            // 플레이어
            {
                var root = new GameObject("Player");
                var cc = root.AddComponent<CharacterController>();
                cc.height = 1.8f; cc.radius = 0.35f; cc.center = new Vector3(0f, 0.9f, 0f);
                root.AddComponent<NetworkObject>();
                var nt = root.AddComponent<OwnerNetworkTransform>();
                StaticTransform(nt, true);
                var pc = root.AddComponent<PlayerController>();
                var camGo = Empty("Camera", root.transform, new Vector3(0f, 1.65f, 0f));
                var cam = camGo.AddComponent<Camera>();
                cam.nearClipPlane = 0.05f; cam.fieldOfView = 70f; cam.enabled = false;
                var al = camGo.AddComponent<AudioListener>(); al.enabled = false;
                var hold = Empty("HoldPoint", camGo.transform, new Vector3(0f, -0.35f, 0.75f));
                var body = Prim(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), Mat("Player", new Color(0.1f, 0.45f, 0.9f)));
                Prim(PrimitiveType.Cube, "Visor", body.transform, new Vector3(0f, 0.6f, 0.4f), new Vector3(0.7f, 0.2f, 0.3f), dark);
                pc.cam = cam; pc.holdPoint = hold.transform; pc.bodyVisual = body;
                var asset = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/Player.prefab");
                Object.DestroyImmediate(root);
                pf.player = asset;
            }

            // 매니저
            {
                var root = new GameObject("GameManagers");
                root.AddComponent<NetworkObject>();
                root.AddComponent<DayCycle>();
                root.AddComponent<Economy>();
                root.AddComponent<DeliveryService>();
                root.AddComponent<CustomerSpawner>();
                root.AddComponent<EventRunner>();
                root.AddComponent<CodexState>();
                root.AddComponent<Announcer>();
                pf.managers = Save<NetworkObject>(root, PrefabDir + "/GameManagers.prefab");
            }

            // 박스
            {
                var root = Prim(PrimitiveType.Cube, "ProductBox", null, Vector3.zero, Vector3.one, cardboard, true);
                Object.DestroyImmediate(root.GetComponent<BoxCollider>());
                root.transform.localScale = Vector3.one;
                Object.DestroyImmediate(root.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(root.GetComponent<MeshFilter>());
                var bc = root.AddComponent<BoxCollider>(); bc.center = new Vector3(0f, 0.17f, 0f); bc.size = new Vector3(0.5f, 0.34f, 0.4f);
                Prim(PrimitiveType.Cube, "Box", root.transform, new Vector3(0f, 0.17f, 0f), new Vector3(0.5f, 0.34f, 0.4f), cardboard);
                var label = Prim(PrimitiveType.Cube, "Label", root.transform, new Vector3(0f, 0.345f, 0f), new Vector3(0.3f, 0.01f, 0.2f), white);
                root.AddComponent<NetworkObject>();
                var box = root.AddComponent<ProductBox>();
                box.labelRenderer = label.GetComponent<Renderer>();
                pf.box = Save<NetworkObject>(root, PrefabDir + "/ProductBox.prefab");
            }

            // 쓰레기봉투
            {
                var root = new GameObject("TrashBag");
                var sc = root.AddComponent<SphereCollider>(); sc.center = new Vector3(0f, 0.22f, 0f); sc.radius = 0.25f;
                Prim(PrimitiveType.Sphere, "Bag", root.transform, new Vector3(0f, 0.22f, 0f), new Vector3(0.45f, 0.45f, 0.45f), Mat("TrashBag", new Color(0.12f, 0.12f, 0.15f)));
                root.AddComponent<NetworkObject>();
                root.AddComponent<TrashBag>();
                pf.trashBag = Save<NetworkObject>(root, PrefabDir + "/TrashBag.prefab");
            }

            // 오염
            {
                var root = new GameObject("Mess");
                var bc = root.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.center = new Vector3(0f, 0.05f, 0f); bc.size = new Vector3(0.9f, 0.1f, 0.9f);
                var pivot = Empty("Visual", root.transform, Vector3.zero);
                Prim(PrimitiveType.Cylinder, "Puddle", pivot.transform, new Vector3(0f, 0.005f, 0f), new Vector3(0.9f, 0.005f, 0.9f), Mat("Mess", new Color(0.45f, 0.3f, 0.1f)));
                root.AddComponent<NetworkObject>();
                var mess = root.AddComponent<Mess>();
                mess.visual = pivot.transform;
                pf.mess = Save<NetworkObject>(root, PrefabDir + "/Mess.prefab");
            }

            // 진열대 3종
            pf.shelfAmbient = Shelf("Shelf_Ambient", StorageType.Ambient, 3, wood);
            pf.shelfChilled = Shelf("Shelf_Chilled", StorageType.Chilled, 3, Mat("Fridge", new Color(0.55f, 0.75f, 0.95f)));
            pf.shelfFrozen = Shelf("Shelf_Frozen", StorageType.Frozen, 2, Mat("Freezer", new Color(0.8f, 0.9f, 1f)));

            // 계산대 2종
            pf.counterShop = Counter("Counter_Shop", Zone.Shop, Mat("CounterShop", new Color(0.2f, 0.55f, 0.35f)));
            pf.counterFood = Counter("Counter_Food", Zone.FoodCourt, Mat("CounterFood", new Color(0.8f, 0.45f, 0.15f)));

            // 주문 태블릿
            {
                var root = new GameObject("OrderTerminal");
                var bc = root.AddComponent<BoxCollider>(); bc.center = new Vector3(0f, 0.6f, 0f); bc.size = new Vector3(0.6f, 1.2f, 0.4f);
                Prim(PrimitiveType.Cube, "Stand", root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.15f, 1f, 0.15f), metal);
                Prim(PrimitiveType.Cube, "Screen", root.transform, new Vector3(0f, 1.1f, 0.05f), new Vector3(0.5f, 0.35f, 0.05f), Mat("Screen", new Color(0.2f, 0.6f, 1f)));
                root.AddComponent<NetworkObject>();
                root.AddComponent<OrderTerminal>();
                pf.terminal = Save<NetworkObject>(root, PrefabDir + "/OrderTerminal.prefab");
            }

            // 쓰레기통
            {
                var root = new GameObject("TrashBin");
                var bc = root.AddComponent<BoxCollider>(); bc.center = new Vector3(0f, 0.45f, 0f); bc.size = new Vector3(0.6f, 0.9f, 0.6f);
                Prim(PrimitiveType.Cylinder, "Can", root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.6f, 0.45f, 0.6f), Mat("Bin", new Color(0.3f, 0.5f, 0.3f)));
                var fillPivot = Empty("Fill", root.transform, new Vector3(0f, 0.02f, 0f));
                Prim(PrimitiveType.Cube, "FillBlock", fillPivot.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.5f, 0.9f, 0.5f), Mat("TrashBag", new Color(0.12f, 0.12f, 0.15f)));
                root.AddComponent<NetworkObject>();
                var bin = root.AddComponent<TrashBin>();
                bin.fillVisual = fillPivot.transform;
                Obstacle(root, new Vector3(0f, 0.45f, 0f), new Vector3(0.6f, 0.9f, 0.6f));
                pf.trashBin = Save<NetworkObject>(root, PrefabDir + "/TrashBin.prefab");
            }

            // 분리수거장
            {
                var root = new GameObject("Dumpster");
                var bc = root.AddComponent<BoxCollider>(); bc.center = new Vector3(0f, 0.6f, 0f); bc.size = new Vector3(2f, 1.2f, 1f);
                Prim(PrimitiveType.Cube, "Body", root.transform, new Vector3(0f, 0.6f, 0f), new Vector3(2f, 1.2f, 1f), Mat("Dumpster", new Color(0.15f, 0.35f, 0.6f)));
                root.AddComponent<NetworkObject>();
                root.AddComponent<Dumpster>();
                pf.dumpster = Save<NetworkObject>(root, PrefabDir + "/Dumpster.prefab");
            }

            // 조리 설비
            foreach (var kv in db.equipment)
            {
                var e = kv.Value;
                var root = new GameObject("Station_" + e.id);
                var bc = root.AddComponent<BoxCollider>(); bc.center = new Vector3(0f, 0.5f, 0f); bc.size = new Vector3(1.2f, 1f, 0.7f);
                var color = e.zone == Zone.Shop ? new Color(0.75f, 0.3f, 0.2f) : new Color(0.5f, 0.5f, 0.55f);
                Prim(PrimitiveType.Cube, "Body", root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(1.2f, 1f, 0.7f), Mat("Station_" + e.id, color));
                var ind = Prim(PrimitiveType.Sphere, "Indicator", root.transform, new Vector3(0f, 1.15f, 0f), new Vector3(0.25f, 0.25f, 0.25f), white);
                root.AddComponent<NetworkObject>();
                var st = root.AddComponent<CookStation>();
                st.data = e;
                st.indicator = ind.GetComponent<Renderer>();
                Obstacle(root, new Vector3(0f, 0.5f, 0f), new Vector3(1.2f, 1f, 0.7f));
                pf.stations[e.id] = Save<NetworkObject>(root, $"{PrefabDir}/Station_{e.id}.prefab");
            }

            pf.all.AddRange(new[] { pf.managers, pf.box, pf.trashBag, pf.mess, pf.shelfAmbient, pf.shelfChilled, pf.shelfFrozen,
                pf.counterShop, pf.counterFood, pf.terminal, pf.trashBin, pf.dumpster });
            pf.all.AddRange(pf.stations.Values);
            return pf;
        }

        static NetworkObject Shelf(string name, StorageType storage, int levels, Material m)
        {
            var root = new GameObject(name);
            float h = 0.5f * levels + 0.2f;
            var bc = root.AddComponent<BoxCollider>(); bc.center = new Vector3(0f, h / 2f, -0.05f); bc.size = new Vector3(1f, h, 0.45f);
            Prim(PrimitiveType.Cube, "Back", root.transform, new Vector3(0f, h / 2f, -0.25f), new Vector3(1f, h, 0.05f), m);
            Prim(PrimitiveType.Cube, "SideL", root.transform, new Vector3(-0.5f, h / 2f, -0.05f), new Vector3(0.04f, h, 0.45f), m);
            Prim(PrimitiveType.Cube, "SideR", root.transform, new Vector3(0.5f, h / 2f, -0.05f), new Vector3(0.04f, h, 0.45f), m);
            root.AddComponent<NetworkObject>();
            for (int i = 0; i < levels; i++)
            {
                float y = 0.1f + i * 0.5f;
                Prim(PrimitiveType.Cube, "Board" + i, root.transform, new Vector3(0f, y, -0.05f), new Vector3(1f, 0.03f, 0.42f), m);
                var slotGo = Empty("Slot" + i, root.transform, new Vector3(0f, y + 0.015f, -0.05f));
                var slot = slotGo.AddComponent<ShelfSlot>();
                slot.storage = storage;
                slot.area = new Vector3(0.9f, 0.4f, 0.36f);
                slot.accessDistance = 0.85f;
                var sbc = slotGo.GetComponent<BoxCollider>();
                sbc.isTrigger = true;
                sbc.center = new Vector3(0f, 0.2f, 0.1f);
                sbc.size = new Vector3(0.96f, 0.44f, 0.5f);
            }
            Obstacle(root, new Vector3(0f, h / 2f, -0.05f), new Vector3(1f, h, 0.45f));
            return Save<NetworkObject>(root, $"{PrefabDir}/{name}.prefab");
        }

        static NetworkObject Counter(string name, Zone zone, Material m)
        {
            var root = new GameObject(name);
            var bc = root.AddComponent<BoxCollider>(); bc.center = new Vector3(0f, 0.5f, 0f); bc.size = new Vector3(2f, 1f, 0.7f);
            Prim(PrimitiveType.Cube, "Body", root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1f, 0.7f), m);
            Prim(PrimitiveType.Cube, "Register", root.transform, new Vector3(0.5f, 1.1f, -0.1f), new Vector3(0.4f, 0.2f, 0.3f), Mat("Dark", new Color(0.1f, 0.1f, 0.1f)));
            var q = Empty("QueueOrigin", root.transform, new Vector3(0f, 0f, 0.9f));
            root.AddComponent<NetworkObject>();
            var c = root.AddComponent<CheckoutCounter>();
            c.zone = zone;
            c.queueOrigin = q.transform;
            Obstacle(root, new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1f, 0.7f));
            return Save<NetworkObject>(root, $"{PrefabDir}/{name}.prefab");
        }

        // ------------------------------------------------------------------ 씬

        static void BuildScene(Db db, Prefabs pf)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 빛
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);

            // 지형과 건물 (정적, 네비메시 굽기 대상)
            var env = new GameObject("Environment").transform;
            var ground = Prim(PrimitiveType.Plane, "Ground", env, new Vector3(0f, 0f, 0f), new Vector3(8f, 1f, 8f), Mat("Asphalt", new Color(0.3f, 0.3f, 0.32f)), true);
            Prim(PrimitiveType.Cube, "Floor", env, new Vector3(0f, -0.04f, 7f), new Vector3(24f, 0.1f, 14f), Mat("Tile", new Color(0.85f, 0.85f, 0.8f)), true);
            var wallM = Mat("Wall", new Color(0.95f, 0.93f, 0.88f));
            void Wall(string n, Vector3 c, Vector3 s) => Prim(PrimitiveType.Cube, n, env, c, s, wallM, true);
            Wall("Wall_Front_L", new Vector3(-7f, 1.5f, 0f), new Vector3(10f, 3f, 0.2f));
            Wall("Wall_Front_R", new Vector3(7f, 1.5f, 0f), new Vector3(10f, 3f, 0.2f));
            Wall("Wall_Back", new Vector3(0f, 1.5f, 14f), new Vector3(24f, 3f, 0.2f));
            Wall("Wall_Left", new Vector3(-12f, 1.5f, 7f), new Vector3(0.2f, 3f, 14f));
            Wall("Wall_Right", new Vector3(12f, 1.5f, 7f), new Vector3(0.2f, 3f, 14f));
            Wall("Wall_Warehouse_L", new Vector3(-6.5f, 1.5f, 10f), new Vector3(11f, 3f, 0.2f));
            Wall("Wall_Warehouse_R", new Vector3(6.5f, 1.5f, 10f), new Vector3(11f, 3f, 0.2f));
            // 식당 테이블
            var tableM = Mat("Table", new Color(0.6f, 0.45f, 0.3f));
            var tables = new[] { new Vector3(2.5f, 0f, 2.5f), new Vector3(2.5f, 0f, 5f), new Vector3(9.5f, 0f, 2.5f), new Vector3(9.5f, 0f, 5f) };
            foreach (var t in tables) Prim(PrimitiveType.Cube, "Table", env, t + Vector3.up * 0.37f, new Vector3(0.9f, 0.75f, 0.9f), tableM, true);
            // 구역 표시
            Prim(PrimitiveType.Cube, "Sign_Shop", env, new Vector3(-6f, 2.8f, 0.15f), new Vector3(4f, 0.4f, 0.05f), Mat("SignShop", Color.green));
            Prim(PrimitiveType.Cube, "Sign_Food", env, new Vector3(6f, 2.8f, 0.15f), new Vector3(4f, 0.4f, 0.05f), Mat("SignFood", Color.yellow));
            // 버스 정류장 표시
            Prim(PrimitiveType.Cube, "BusStopPole", env, new Vector3(8f, 1.2f, -9f), new Vector3(0.15f, 2.4f, 0.15f), Mat("Pole", Color.gray));

            // 위치 정보
            var layoutGo = new GameObject("LevelLayout");
            var layout = layoutGo.AddComponent<LevelLayout>();
            layout.playerSpawn = Empty("PlayerSpawn", layoutGo.transform, new Vector3(-1f, 0.1f, 8.5f), 180f).transform;
            layout.entrance = Empty("Entrance", layoutGo.transform, new Vector3(0f, 0f, -4f)).transform;
            layout.busStop = Empty("BusStop", layoutGo.transform, new Vector3(7f, 0f, -8f)).transform;
            layout.exit = Empty("Exit", layoutGo.transform, new Vector3(-6f, 0f, -9f)).transform;
            layout.deliveryArea = Empty("DeliveryArea", layoutGo.transform, new Vector3(-10.5f, 0f, 11f)).transform;
            var seats = new List<Transform>();
            foreach (var t in tables)
            {
                seats.Add(Empty("Seat", layoutGo.transform, t + new Vector3(-1.1f, 0f, 0f), 90f).transform);
                seats.Add(Empty("Seat", layoutGo.transform, t + new Vector3(1.1f, 0f, 0f), -90f).transform);
            }
            layout.seats = seats.ToArray();

            // 설비 위치 (호스트가 런타임에 스폰)
            var markers = new GameObject("SpawnMarkers").transform;
            int n = 0;
            void M(NetworkObject prefab, Vector3 pos, float yaw)
            {
                var g = Empty($"{prefab.name}_{++n:00}", markers, pos, yaw);
                g.AddComponent<SpawnMarker>().prefab = prefab;
            }
            // 편의점: 앞줄(상온) z=3 정면 +z, 뒷줄(냉장/냉동) z=7 정면 -z
            M(pf.shelfAmbient, new Vector3(-10.5f, 0f, 3f), 0f);
            M(pf.shelfAmbient, new Vector3(-9.5f, 0f, 3f), 0f);
            M(pf.shelfAmbient, new Vector3(-8.5f, 0f, 3f), 0f);
            M(pf.shelfAmbient, new Vector3(-7.5f, 0f, 3f), 0f);
            M(pf.shelfChilled, new Vector3(-10.5f, 0f, 7f), 180f);
            M(pf.shelfChilled, new Vector3(-9.5f, 0f, 7f), 180f);
            M(pf.shelfChilled, new Vector3(-8.5f, 0f, 7f), 180f);
            M(pf.shelfFrozen, new Vector3(-7.5f, 0f, 7f), 180f);
            M(pf.counterShop, new Vector3(-2.5f, 0f, 5f), -90f);
            M(pf.stations["odeng"], new Vector3(-1.2f, 0f, 3f), -90f);
            M(pf.stations["sweet_potato"], new Vector3(-1.2f, 0f, 7f), -90f);
            M(pf.trashBin, new Vector3(-5f, 0f, 0.6f), 0f);
            // 식당가
            M(pf.counterFood, new Vector3(6f, 0f, 7f), 180f);
            M(pf.stations["ramen"], new Vector3(4.5f, 0f, 9.2f), 180f);
            M(pf.stations["udon"], new Vector3(7.5f, 0f, 9.2f), 180f);
            M(pf.trashBin, new Vector3(11.3f, 0f, 0.8f), 0f);
            // 창고 (물류)
            M(pf.terminal, new Vector3(-3f, 0f, 13.5f), 180f);
            M(pf.dumpster, new Vector3(10f, 0f, 12.8f), 180f);

            // 네비메시 (정적 지형만 굽고, 설비는 NavMeshObstacle로 깎는다)
            var navGo = new GameObject("Navigation");
            var surface = navGo.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            string navPath = SceneDir + "/NavMesh_RestStop.asset";
            AssetDatabase.DeleteAsset(navPath);
            AssetDatabase.CreateAsset(surface.navMeshData, navPath);

            // 메뉴 카메라
            var menuCam = new GameObject("MenuCamera").AddComponent<Camera>();
            menuCam.transform.SetPositionAndRotation(new Vector3(0f, 16f, -14f), Quaternion.Euler(45f, 0f, 0f));
            menuCam.gameObject.AddComponent<AudioListener>();

            // 네트워크 매니저
            var nmGo = new GameObject("NetworkManager");
            var nm = nmGo.AddComponent<NetworkManager>();
            var transport = nmGo.AddComponent<UnityTransport>();
            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = transport;
            nm.NetworkConfig.PlayerPrefab = pf.player;
            nm.NetworkConfig.ConnectionApproval = true;

            // 게임 루트
            var rootGo = new GameObject("GameRoot");
            var root = rootGo.AddComponent<GameRoot>();
            root.products = db.products;
            root.customers = db.customers;
            root.equipment = new List<EquipmentData>(db.equipment.Values).ToArray();
            root.networkPrefabs = new List<NetworkObject>(pf.all);
            foreach (var c in db.customers.items) root.networkPrefabs.Add(c.prefab);
            root.managersPrefab = pf.managers;
            root.boxPrefab = pf.box.GetComponent<ProductBox>();
            root.trashBagPrefab = pf.trashBag.GetComponent<TrashBag>();
            root.messPrefab = pf.mess.GetComponent<Mess>();
            root.layout = layout;
            root.menuCamera = menuCam;
            rootGo.AddComponent<HUD>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = new List<EditorBuildSettingsScene> { new(ScenePath, true) };
            foreach (var s in EditorBuildSettings.scenes) if (s.path != ScenePath) scenes.Add(s);
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
