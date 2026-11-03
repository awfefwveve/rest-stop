using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RestStop
{
    /// <summary>
    /// 프로토타입 HUD (IMGUI). 상태, 조준점, 안내 문구, 알림, 희귀 손님 배너, 말풍선, 정산.
    /// 출시 전 uGUI/UI Toolkit + TextMeshPro로 교체한다.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        struct Note { public string text; public float until; }

        static readonly List<Note> _notes = new();
        static string _banner;
        static Color _bannerColor;
        static float _bannerUntil;

        GUIStyle _small, _mid, _big, _center, _bubble;

        public static void Notify(string text)
        {
            _notes.Add(new Note { text = text, until = Time.time + 5f });
            if (_notes.Count > 6) _notes.RemoveAt(0);
        }

        public static void Banner(Rarity r, string name)
        {
            _bannerColor = RarityColor(r);
            _banner = $"{Loc.Get("rarity_" + r)}  {name}";
            _bannerUntil = Time.time + (r == Rarity.Legendary ? 5f : 3f);
        }

        public static Color RarityColor(Rarity r) => r switch
        {
            Rarity.Rare => new Color(0.35f, 0.65f, 1f),
            Rarity.Epic => new Color(0.75f, 0.4f, 1f),
            Rarity.Legendary => new Color(1f, 0.75f, 0.2f),
            _ => Color.white
        };

        void Styles()
        {
            if (_small != null) return;
            _small = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            _mid = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            _big = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _center = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            _bubble = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        }

        void Update()
        {
            var kb = Keyboard.current;
            var nm = NetworkManager.Singleton;
            if (kb == null || nm == null || !nm.IsServer || DayCycle.Instance == null) return;
            if (kb.f5Key.wasPressedThisFrame) DayCycle.Instance.ServerOpen();
            if (kb.f6Key.wasPressedThisFrame) DayCycle.Instance.ServerNextDay();
        }

        void OnGUI()
        {
            var me = PlayerController.Local;
            if (me == null || DayCycle.Instance == null || Economy.Instance == null) return;
            Styles();

            var day = DayCycle.Instance;
            var eco = Economy.Instance;
            var nm = NetworkManager.Singleton;

            // 상태
            GUILayout.BeginArea(new Rect(16, 16, 330, 220), GUI.skin.box);
            GUILayout.Label($"{Loc.Get("hud_day", day.Day.Value)}  ·  {Loc.Get("phase_" + day.Phase.Value)}  ·  {DayCycle.Clock(day.Hour.Value)}", _mid);
            GUILayout.Label(Loc.Get("hud_money", eco.Money.Value.ToString("N0")), _mid);
            GUILayout.Label(Loc.Get("hud_rep", Mathf.RoundToInt(eco.Reputation.Value)) + "   " + Loc.Get("hud_clean", Mathf.RoundToInt(eco.Cleanliness.Value)), _small);
            GUILayout.Label(Loc.Get("hud_level", eco.Level.Value) + "   " + Loc.Get("hud_players", PlayerController.All.Count, GameRoot.MaxPlayers), _small);
            if (CodexState.Instance != null)
                GUILayout.Label(Loc.Get("hud_codex", CodexState.Instance.Seen.Count, GameRoot.Instance.customers.items.Count), _small);
            if (DeliveryService.Instance != null && DeliveryService.Instance.PendingBoxes.Value > 0)
                GUILayout.Label(Loc.Get("order_pending", DeliveryService.Instance.PendingBoxes.Value), _small);
            GUILayout.EndArea();

            // 조준점 + 안내
            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            GUI.Label(new Rect(cx - 10, cy - 12, 20, 24), "+", _center);
            if (!string.IsNullOrEmpty(me.HoverPrompt))
                GUI.Label(new Rect(cx - 300, cy + 24, 600, 30), me.HoverPrompt, _center);
            var held = me.Held;
            if (held != null)
                GUI.Label(new Rect(cx - 300, Screen.height - 90, 600, 30), Loc.Get("hud_holding", held.DisplayName), _center);
            GUI.Label(new Rect(16, Screen.height - 34, 900, 26), Loc.Get("hud_controls"), _small);

            // 알림
            float y = 16;
            for (int i = _notes.Count - 1; i >= 0; i--)
            {
                if (_notes[i].until < Time.time) { _notes.RemoveAt(i); continue; }
            }
            foreach (var n in _notes)
            {
                GUI.Box(new Rect(Screen.width - 436, y, 420, 30), n.text, _bubble);
                y += 34;
            }

            // 희귀 손님 배너
            if (_banner != null && Time.time < _bannerUntil)
            {
                var old = GUI.color;
                GUI.color = _bannerColor;
                GUI.Label(new Rect(cx - 400, 90, 800, 50), _banner, _big);
                GUI.color = old;
            }

            DrawBubbles(me);
            DrawPhasePanel(day, eco, nm.IsServer);
        }

        void DrawBubbles(PlayerController me)
        {
            var cam = me.cam;
            foreach (var c in CustomerAI.All)
            {
                if (c == null) continue;
                var d = c.Data;
                if (d == null) continue;
                var head = c.transform.position + Vector3.up * 2.1f;
                var sp = cam.WorldToScreenPoint(head);
                if (sp.z <= 0f || sp.z > 14f) continue;
                float gx = sp.x, gy = Screen.height - sp.y;

                string text = c.Bubble.Value.Length > 0 ? Loc.Get(c.Bubble.Value.ToString()) : null;
                if (d.rarity >= Rarity.Rare || text != null)
                {
                    var old = GUI.color;
                    GUI.color = RarityColor(d.rarity);
                    GUI.Label(new Rect(gx - 120, gy - 22, 240, 22), Loc.Get(d.nameKey), _center);
                    GUI.color = old;
                }
                if (text != null) GUI.Box(new Rect(gx - 120, gy - 64, 240, 40), text, _bubble);

                if (c.State.Value == CustomerState.Queued || c.State.Value == CustomerState.ToCounter)
                {
                    float w = 60f * c.Patience01.Value;
                    var old = GUI.color;
                    GUI.color = Color.Lerp(Color.red, Color.green, c.Patience01.Value);
                    GUI.DrawTexture(new Rect(gx - 30, gy + 2, w, 5), Texture2D.whiteTexture);
                    GUI.color = old;
                }
            }
        }

        void DrawPhasePanel(DayCycle day, Economy eco, bool host)
        {
            float cx = Screen.width / 2f;
            if (day.Phase.Value == DayPhase.Prep)
            {
                GUILayout.BeginArea(new Rect(cx - 220, 20, 440, 60), GUI.skin.box);
                GUILayout.Label(Loc.Get(host ? "hud_open_key" : "hud_wait_host"), _center);
                GUILayout.EndArea();
            }
            else if (day.Phase.Value == DayPhase.Closed)
            {
                if (Cursor.lockState == CursorLockMode.Locked && host) PlayerController.LockCursor(false);
                var r = eco.LastReport.Value;
                GUILayout.BeginArea(new Rect(cx - 230, 120, 460, 470), GUI.skin.box);
                GUILayout.Label(Loc.Get("report_title", r.day), _big);
                Row("report_revenue", r.revenue);
                Row("report_cogs", -r.cogs);
                Row("report_waste", -r.waste);
                Row("report_equipment", -r.equipment);
                Row("report_fixed", -r.fixedCost);
                Row("report_events", r.events);
                GUILayout.Space(6);
                Row("report_net", r.net);
                GUILayout.Space(6);
                GUILayout.Label(Loc.Get("report_people", r.customers, r.angry, r.newFaces), _mid);
                GUILayout.Label(Loc.Get("hud_rep", Mathf.RoundToInt(eco.Reputation.Value)) + "   " + Loc.Get("hud_clean", Mathf.RoundToInt(eco.Cleanliness.Value)), _mid);
                GUILayout.FlexibleSpace();
                if (host)
                {
                    if (GUILayout.Button(Loc.Get("btn_next_day"), GUILayout.Height(44)))
                    {
                        day.ServerNextDay();
                        PlayerController.LockCursor(true);
                    }
                }
                else GUILayout.Label(Loc.Get("hud_wait_host"), _center);
                GUILayout.EndArea();
            }
        }

        void Row(string key, int value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.Get(key), _mid);
            GUILayout.FlexibleSpace();
            GUILayout.Label((value >= 0 ? "" : "-") + Mathf.Abs(value).ToString("N0"), _mid);
            GUILayout.EndHorizontal();
        }
    }
}
