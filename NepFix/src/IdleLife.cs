using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;

namespace NepFix
{
    /// Живой idle: жесты из анимаций самой игры, когда героиня стоит, плюс дыхание и взгляд.
    internal static class IdleLife
    {
        static Settings S => Plugin.S;
        static readonly System.Random rnd = new();

        // цепочки жестов: имена состояний без префикса-номера и суффикса персонажа
        static readonly string[][] Gestures =
        {
            new[] { "10021_DG_StnbyMotion_Start" },
            new[] { "40161_GN_WaistHands_Start", "40162_GN_WaistHands_Loop", "40162_GN_WaistHands_Loop", "40163_GN_WaistHands_End" },
            new[] { "40031_GN_VerticalSahkeHead_Start" },
            new[] { "40041_GN_SideSahkeHead_Start" },
            new[] { "40061_GN_Joy_Start", "40062_GN_Joy_Loop", "40063_GN_Joy_End" },
        };
        static readonly float[] GestureWeight = { 3f, 3f, 1.5f, 1f, 0.7f };

        class Unit
        {
            public Animator anim; public Transform root;
            public string id; public int idle, stnby;
            public List<int[]> gestures = new();
            public float idleFor, nextAt;
            public int[] seq; public int step; public float stepStart;
            public Vector3 lastPos; public float speed; public int ticks;
            public MapUnitBaseComponent mub; public int restId = -1;
            public AnimatorOverrideController ovr; public RuntimeAnimatorController orig;
            public AnimationClip carrierClip; public int carrierHash; public AnimationClip customPlaying;
            public float customUntil, restoreAt; public Transform hips; public bool customBroken, rootMotionOrig; public float waitOrig = -1, waitRndOrig;
            public PlayableGraph graph; public AnimationPlayableOutput output; public bool hasGraph; public float fadeOutStart = -1; public float lastCheckLog; public List<int[]> ids = new(); public int[] idSeq;
            public Transform head, neck, chest, spine, eyeL, eyeR;
            public float lookW, breathPhase;
            public Quaternion lookDelta = Quaternion.identity;
        }
        static readonly Dictionary<IntPtr, Unit> units = new();
        static float nextScan;
        static readonly HashSet<string> seen = new();
        public static int Count, Playing;
        public static string Last = "";

        static readonly string[] AllStates = { "10011_DG_Idle","10021_DG_StnbyMotion_Start","10041_DG_Walk_Start","10042_DG_Walk_Loop","10043_DG_Walk_End","10051_DG_RunFirst_Loop","10052_DG_RunFirst_End","10061_DG_RunSecond_Loop","10062_DG_RunSecond_End","10071_DG_Jump_Start","10072_DG_JumpDescending_Start","10073_DG_Jump_End","10091_DG_Climbing_Start","10092_DG_Climbing_Up","10093_DG_Climbing_Loop","10094_DG_Climbing_End","10131_DG_SymbolAttak_Start","10141_DG_PutOutWeapon_Start","10151_DG_PutAwayWeapon_Start","20011_BT_Idle","20021_BT_Run_Loop","20022_BT_Run_End","20031_BT_Weak_Start","20032_BT_Weak_Loop","20033_BT_Weak_End","20041_BT_Stan_Start","20042_BT_Stan_Loop","20043_BT_Stan_End","20051_BT_KnockBack_S_Start","20052_BT_KnockBack_S_Loop","20053_BT_KnockBack_S_End","20061_BT_KnockBack_M_Start","20062_BT_KnockBack_M_Loop","20063_BT_KnockBack_M_End","20071_BT_KnockBack_L_Start","20072_BT_KnockBack_L_Loop","20073_BT_KnockBack_L_End","20081_BT_ReturnDown_Start","20091_BT_Dead_Start","20101_BT_Gurad_Start","20102_BT_Gurad_Loop","20103_BT_Gurad_End","20111_BT_GuradBreak_Start","20112_BT_GuradBreak_Loop","20113_BT_GuradBreak_End","20131_BT_ForwardStep_Start","20141_BT_BehindStep_Start","20151_BT_RightStep_Start","20161_BT_LeftStep_Start","20171_BT_CharacterChange_Start","20172_BT_CharacterChange_Loop","20173_BT_CharacterChange_End","20181_BT_TransBefore_Start","20201_BT_MagicChant_Start","20202_BT_MagicChant_Loop","20203_BT_MagicChant_End","30021_ATK_FirstRapid_Start","30022_ATK_FirstRapid_Loop","30023_ATK_FirstRapid_End","30031_ATK_FirstPower_Start","30041_ATK_FirstBreak_Start","30042_ATK_FirstBreak_Loop","30043_ATK_FirstBreak_End","30051_ATK_Comb01_Start","30071_ATK_Comb02_Start","30072_ATK_Comb02_Loop","30073_ATK_Comb02_End","30091_ATK_Comb03_Start","30111_ATK_Comb04_Start","30112_ATK_Comb04_Loop","30113_ATK_Comb04_End","30131_ATK_Comb05_Start","30132_ATK_Comb05_Loop","30133_ATK_Comb05_End","30151_ATK_Comb06_Start","30171_ATK_Comb07_Start","30191_ATK_Comb08_Start","30211_ATK_Comb09_Start","30231_ATK_Comb10_Start","30401_ATK_SpSkill01_Start","30402_ATK_SpSkill01_Loop","30403_ATK_SpSkill01_End","30501_ATK_SpSkill02_Start","30502_ATK_SpSkill02_Loop","30503_ATK_SpSkill02_Attack","30504_ATK_SpSkill02_AttackLoop","30505_ATK_SpSkill02_End","30601_ATK_SpSkill03_Start","30602_ATK_SpSkill03_Loop","30603_ATK_SpSkill03_Attack","30604_ATK_SpSkill03_AttackLoop","30605_ATK_SpSkill03_End","40031_GN_VerticalSahkeHead_Start","40041_GN_SideSahkeHead_Start","40051_GN_Scary_Start","40052_GN_Scary_Loop","40053_GN_Scary_End","40061_GN_Joy_Start","40062_GN_Joy_Loop","40063_GN_Joy_End","40071_GN_Surprise_Start","40072_GN_Surprise_Loop","40073_GN_Surprise_End","40081_GN_Angry_Start","40082_GN_Angry_Loop","40083_GN_Angry_End","40091_GN_Sad_Start","40092_GN_Sad_Loop","40093_GN_Sad_End","40161_GN_WaistHands_Start","40162_GN_WaistHands_Loop","40163_GN_WaistHands_End","50000_ExeDrive","10121_DG_Press_Start","20231_BT_ForwardWalk_Start","20232_BT_ForwardWalk_Loop","20233_BT_ForwardWalk_End","20241_BT_BehindWalk_Start","20242_BT_BehindWalk_Loop","20243_BT_BehindWalk_End","20251_BT_RightWalk_Start","20252_BT_RightWalk_Loop","20253_BT_RightWalk_End","20261_BT_LeftWalk_Start","20262_BT_LeftWalk_Loop","20263_BT_LeftWalk_End","20301_BT_Counter_Start","20302_BT_Counter_Loop","20303_BT_Counter_End","90001_Accecary","10171_DG_RunFirst_Start","10172_DG_RunFirst_Loop","10173_DG_RunFirst_End","25011_BT_LinkChain_Raise_CutInStart","25013_BT_LinkChain_Raise_Loop","25014_BT_LinkChain_Raise_End","25021_BT_LinkChain_Swingdown_CutInStart","25023_BT_LinkChain_Swingdown_Loop","25024_BT_LinkChain_Swingdown_End","25031_BT_LinkChain_Stickout_CutInStart","25033_BT_LinkChain_Stickout_Loop","25034_BT_LinkChain_Stickout_End","29010_BT_BattleFinish_Start","29011_BT_BattleFinish_Loop","29012_BT_BattleFinish_WorkStart","29020_BT_BattleEnd_Start","29021_BT_BattleEnd_Loop","29022_BT_BattleEnd_WorkStart","50101_ATK_LinkDrive_Start","50102_ATK_LinkDrive_Loop","50103_ATK_LinkDrive_Center","50104_ATK_LinkDrive_Side","20132_BT_ForwardStep_Loop","20133_BT_ForwardStep_End","20142_BT_BehindStep_Loop","20143_BT_BehindStep_End","20152_BT_RightStep_Loop","20153_BT_RightStep_End","20162_BT_LeftStep_Loop","20163_BT_LeftStep_End" };
        static readonly Dictionary<string, Dictionary<int, string>> stateNames = new();
        static string StateName(string id, int h)
        {
            if (!stateNames.TryGetValue(id, out var d)) { d = new(); foreach (var n in AllStates) d[Animator.StringToHash(n + "_" + id)] = n; stateNames[id] = d; }
            return d.TryGetValue(h, out var r) ? r : h.ToString();
        }
        static float nextLog, nextDiagLog;
        public static string Diag = "";

        static readonly Regex CtrlName = new(@"(C\d{4})", RegexOptions.Compiled);

        public static void Update()
        {
            if (!S.IdleGestures.Value && !S.IdleBreath.Value && !S.IdleLook.Value) { if (units.Count > 0) units.Clear(); return; }
            float now = Time.unscaledTime;
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 1.5f; Scan(); }
            if (!S.IdleGestures.Value) return;
            Playing = 0;
            var dead = new List<IntPtr>();
            foreach (var kv in units)
            {
                var u = kv.Value;
                try
                {
                    if (u.anim == null || u.root == null) { Drop(dead, kv.Key, u, "объект удалён"); continue; }
                    u.ticks++;
                    var pos = u.anim.transform.position;
                    // только горизонтальная скорость: по высоте корень постоянно подрагивает из-за прижатия к земле
                    var dp = pos - u.lastPos; dp.y = 0;
                    float inst = dp.magnitude / Math.Max(1e-4f, Time.unscaledDeltaTime);
                    u.speed += (inst - u.speed) * 0.2f;
                    float moved = u.speed;
                    u.lastPos = pos;
                    var st = u.anim.GetCurrentAnimatorStateInfo(0);
                    bool trans = u.anim.IsInTransition(0);

                    if (u.restoreAt > 0 && now >= u.restoreAt) { RestoreOverride(u); u.restoreAt = 0; }
                    if (u.customPlaying != null)
                    {
                        Playing++;
                        float fade = Math.Clamp(S.IdleBlend.Value, 0.1f, 1f);
                        if (u.hips != null)
                        {
                            var hp = u.hips.position;
                            if (float.IsNaN(hp.x) || (hp - u.anim.transform.position).magnitude > 2.5f)
                            {
                                Plugin.L.LogWarning($"Анимации: {u.anim.name}: поза сломалась во время {u.customPlaying.name}, отключаю сторонние для неё");
                                StopGraph(u); u.customPlaying = null; u.customBroken = true; u.anim.Rebind(); HoldGameIdle(u, false);
                                u.idleFor = 0; u.nextAt = Next(); continue;
                            }
                        }
                        if (u.fadeOutStart < 0 && (moved > 0.8f || now >= u.customUntil || (!trans && st.m_Name != u.idle && st.m_Name != u.stnby)))
                        {
                            u.fadeOutStart = now;
                            Plugin.L.LogInfo($"Анимации: {u.anim.name} сторонняя заканчивается: {(moved > 0.8f ? "героиня пошла" : now >= u.customUntil ? "по времени" : "игра включила " + StateName(u.id, st.m_Name))}");
                        }
                        float w = Math.Clamp((now - u.stepStart) / fade, 0f, 1f);
                        if (u.fadeOutStart >= 0) w = Math.Min(w, 1f - Math.Clamp((now - u.fadeOutStart) / (moved > 0.8f ? 0.2f : fade), 0f, 1f));
                        w = w * w * (3f - 2f * w);
                        if (u.hasGraph) u.output.GetHandle().SetWeight(w);
                        if (u.fadeOutStart >= 0 && now - u.fadeOutStart >= fade)
                        {
                            StopGraph(u); u.customPlaying = null; u.fadeOutStart = -1;
                            u.anim.applyRootMotion = u.rootMotionOrig; HoldGameIdle(u, false);
                            u.idleFor = 0; u.nextAt = Next();
                        }
                        continue;
                    }
                    if (u.seq != null)
                    {
                        Playing++;
                        int want = u.seq[u.step];
                        // игра переключила анимацию сама (пошли, начался бой, сцена): жест прерываем
                        if (!trans && st.m_Name != want && now - u.stepStart > 0.5f) { Plugin.L.LogInfo($"Анимации: {u.anim.name} жест прерван, игра включила {StateName(u.id, st.m_Name)}"); u.seq = null; u.idleFor = 0; u.nextAt = Next(); HoldGameIdle(u, false); continue; }
                        if (moved > 0.8f) { u.anim.CrossFadeInFixedTime(u.idle, 0.25f, 0); u.seq = null; u.idleFor = 0; u.nextAt = Next(); HoldGameIdle(u, false); continue; }
                        if (!trans && st.m_Name == want && st.normalizedTime >= StepEnd(u)) Advance(u, now);
                        continue;
                    }

                    // игра сама чередует обычный idle и свою анимацию ожидания: обе считаем простоем
                    bool calmState = st.m_Name == u.idle || st.m_Name == u.stnby;
                    if (calmState && moved < 0.4f) u.idleFor += Time.unscaledDeltaTime; else u.idleFor = 0;
                    if (!trans && st.m_Name == u.idle && u.idleFor >= u.nextAt && u.gestures.Count > 0)
                    {
                        if (S.CustomIdles.Value && CustomIdles.Clips.Count > 0 && !u.customBroken && rnd.NextDouble() < S.CustomShare.Value)
                        { StartCustom(u, now); }
                        else
                        {
                            u.seq = Pick(u); u.step = -1;
                            HoldGameIdle(u, true);
                            Plugin.L.LogInfo($"Анимации: {u.anim.name} начинает жест из {u.seq.Length} шагов");
                            Advance(u, now);
                        }
                    }
                }
                catch (Exception e) { Drop(dead, kv.Key, u, e.GetType().Name + ": " + e.Message); }
            }
            foreach (var d in dead) units.Remove(d);
            Count = units.Count;
            if (Time.unscaledTime >= nextLog)
            {
                nextLog = Time.unscaledTime + 1f;
                var sb = new System.Text.StringBuilder();
                foreach (var u in units.Values)
                {
                    try
                    {
                        var st = u.anim.GetCurrentAnimatorStateInfo(0);
                        if (false) { sb.Append($"{u.anim.name}: анимация {u.mub.GetAnimation()}, скорость {u.speed:0.00}, стоит {u.idleFor:0.0}/{u.nextAt:0} с{(u.idSeq != null ? ", жест" : "")}; "); continue; }
                        sb.Append($"{u.id}: {(st.m_Name == u.stnby ? "ожидание игры" : StateName(u.id, st.m_Name))}, скорость {u.speed:0.00}, стоит {u.idleFor:0.0}/{u.nextAt:0} с{(u.seq != null ? ", жест" : u.customPlaying != null ? ", " + u.customPlaying.name : "")}; ");
                    }
                    catch { }
                }
                Diag = sb.ToString();
                if (Time.unscaledTime >= nextDiagLog) { nextDiagLog = Time.unscaledTime + 4f; Plugin.L.LogInfo($"Анимации, состояние (timeScale {Time.timeScale:0.00}, LateUpdate {LateTicks}): " + Diag); }
            }
        }

        const int IdIdle = 10011, IdStandby = 10021;

        // управление через систему анимаций самой игры: она не перебивает свои же команды
        static void GameApiStep(Unit u, float moved, float now)
        {
            var m = u.mub;
            int cur = m.GetAnimation();
            if (u.idSeq != null)
            {
                Playing++;
                int want = u.idSeq[u.step];
                if (cur != want || moved > 0.8f)
                {
                    Plugin.L.LogInfo($"Анимации: {u.anim.name} жест {want} прерван, игра включила {cur}, скорость {moved:0.00}");
                    u.idSeq = null; u.idleFor = 0; u.nextAt = Next(); return;
                }
                bool loopNext = u.step + 1 < u.idSeq.Length && u.idSeq[u.step + 1] == want;
                bool done = loopNext ? now - u.stepStart > 1.6f : (m.IsAnimationEnd() || now - u.stepStart > 8f);
                if (done && now - u.stepStart > 0.3f) GameApiAdvance(u, now);
                return;
            }
            // номер анимации покоя у игры может отличаться, поэтому покой определяем по отсутствию движения
            if (moved < 0.4f && Time.timeScale > 0.01f) u.idleFor += Time.unscaledDeltaTime; else { u.idleFor = 0; u.restId = cur; }
            if (u.idleFor < 0.5f) u.restId = cur;
            bool calm = cur == IdIdle || cur == IdStandby || cur == u.restId;
            if (!calm) u.idleFor = 0;
            if (u.idleFor >= u.nextAt && u.ids.Count > 0)
            {
                double total = 0; foreach (var g in u.ids) total += g[^1];
                double r = rnd.NextDouble() * total; int[] pick = u.ids[0];
                foreach (var g in u.ids) { r -= g[^1]; if (r <= 0) { pick = g; break; } }
                u.idSeq = pick[..^1]; u.step = -1;
                Plugin.L.LogInfo($"Анимации: {u.anim.name} начинает жест {string.Join("→", u.idSeq)}");
                GameApiAdvance(u, now);
            }
        }

        static void GameApiAdvance(Unit u, float now)
        {
            u.step++;
            float fade = Math.Clamp(S.IdleBlend.Value, 0.1f, 1f);
            if (u.step >= u.idSeq.Length)
            {
                u.mub.SetAnimation(u.restId >= 0 ? u.restId : IdIdle, MapUnitBaseComponent.AnimationBitMode.DEFAULT, fade);
                u.idSeq = null; u.idleFor = 0; u.nextAt = Next();
                return;
            }
            bool same = u.step > 0 && u.idSeq[u.step] == u.idSeq[u.step - 1];
            if (!same) u.mub.SetAnimation(u.idSeq[u.step], MapUnitBaseComponent.AnimationBitMode.OVERWRITE, fade);
            u.stepStart = now;
        }

        static readonly HashSet<string> dropLogged = new();
        static void Drop(List<IntPtr> dead, IntPtr k, Unit u, string why)
        {
            dead.Add(k);
            StopGraph(u);
            Last = $"{u.id}: снята с управления, {why}";
            if (dropLogged.Add(why)) Plugin.L.LogWarning("Анимации: " + Last);
        }

        static void StartCustom(Unit u, float now)
        {
            var clip = CustomIdles.Clips[rnd.Next(CustomIdles.Clips.Count)];
            try
            {
                StopGraph(u);
                // отдельный граф поверх аниматора игры: его контроллер не трогаем, только смешиваем по весу
                u.graph = PlayableGraph.Create("NepIdle_" + u.anim.name);
                u.graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                u.output = AnimationPlayableOutput.Create(u.graph, "NepIdle", u.anim);
                var p = AnimationClipPlayable.Create(u.graph, clip);
                p.SetApplyFootIK(true);
                u.output.GetHandle().SetSourcePlayable(p.GetHandle(), 0);
                u.output.GetHandle().SetWeight(0f);
                u.graph.Play();
                u.hasGraph = true;
                u.rootMotionOrig = u.anim.applyRootMotion;
                u.anim.applyRootMotion = false;
                u.customPlaying = clip; u.stepStart = now; u.fadeOutStart = -1;
                HoldGameIdle(u, true);
                float len = Math.Max(0.5f, clip.length);
                int loops = Math.Max(1, (int)Math.Round(S.CustomDuration.Value / len));
                u.customUntil = now + len * loops;
                Plugin.L.LogInfo($"Анимации: {u.anim.name} играет стороннюю {clip.name}, {len:0.0} с x{loops}");
            }
            catch (Exception e) { Plugin.L.LogWarning("Анимации: сторонняя: " + e); StopGraph(u); u.idleFor = 0; u.nextAt = Next(); }
        }

        static void StopGraph(Unit u)
        {
            if (!u.hasGraph) return;
            try { if (u.graph.IsValid()) u.graph.Destroy(); } catch { }
            u.hasGraph = false;
        }

        static void HoldGameIdle(Unit u, bool hold)
        {
            if (u.mub == null) return;
            try
            {
                if (hold)
                {
                    if (u.waitOrig < 0) { u.waitOrig = u.mub.animation_idle_wait_time_; u.waitRndOrig = u.mub.animation_idle_wait_time_random_; }
                    u.mub.animation_idle_wait_time_ = 9999f; u.mub.animation_idle_wait_time_random_ = 0f;
                }
                else if (u.waitOrig >= 0)
                {
                    u.mub.animation_idle_wait_time_ = u.waitOrig; u.mub.animation_idle_wait_time_random_ = u.waitRndOrig; u.waitOrig = -1;
                }
            }
            catch { }
        }

        static void AbortCustom(Unit u, bool broken)
        {
            try
            {
                RestoreOverride(u);
                if (u.orig != null) { u.anim.runtimeAnimatorController = u.orig; u.ovr = null; }
                if (broken) u.anim.Rebind();
                u.anim.Play(u.idle, 0, 0f);
            }
            catch { }
            try { u.anim.applyRootMotion = u.rootMotionOrig; } catch { }
            HoldGameIdle(u, false);
            u.customPlaying = null; u.idleFor = 0; u.nextAt = Next(); u.customBroken = broken;
        }

        static void RestoreOverride(Unit u)
        {
            try { if (u.ovr != null && u.carrierClip != null) u.ovr[u.carrierClip] = null; } catch { }
        }

        static float StepEnd(Unit u)
        {
            bool loopStep = u.step + 1 < u.seq.Length && u.seq[u.step + 1] == u.seq[u.step];
            return loopStep ? 0.98f : 0.88f;
        }

        static void Advance(Unit u, float now)
        {
            u.step++;
            float fade = Math.Clamp(S.IdleBlend.Value, 0.1f, 1f);
            if (u.step >= u.seq.Length)
            {
                u.anim.CrossFadeInFixedTime(u.idle, fade, 0);
                u.seq = null; u.idleFor = 0; u.nextAt = Next(); HoldGameIdle(u, false);
                return;
            }
            bool same = u.step > 0 && u.seq[u.step] == u.seq[u.step - 1];
            if (same) u.anim.CrossFadeInFixedTime(u.seq[u.step], 0.05f, 0, 0f);
            else u.anim.CrossFadeInFixedTime(u.seq[u.step], fade, 0);
            u.stepStart = now;
        }

        static float Next()
        {
            float a = Math.Max(2f, S.IdleDelayMin.Value), b = Math.Max(a + 0.5f, S.IdleDelayMax.Value);
            return a + (float)rnd.NextDouble() * (b - a);
        }

        static int[] Pick(Unit u)
        {
            double total = 0; foreach (var g in u.gestures) total += g[g.Length - 1];
            double r = rnd.NextDouble() * total;
            foreach (var g in u.gestures) { r -= g[g.Length - 1]; if (r <= 0) return g[..^1]; }
            return u.gestures[0][..^1];
        }

        static void Scan()
        {
            if (S.CustomIdles.Value) CustomIdles.Load();
            try
            {
                var arr = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Animator>());
                if (arr == null) return;
                foreach (var o in arr)
                {
                    var a = o.TryCast<Animator>(); if (a == null || units.ContainsKey(a.Pointer)) continue;
                    var ctrl = a.runtimeAnimatorController;
                    string cname = ctrl != null ? ctrl.name ?? "" : "";
                    string key = a.name + "|" + cname;
                    if (seen.Add(key)) Plugin.L.LogInfo($"Анимации: аниматор '{a.name}', контроллер '{cname}', human {a.isHuman}");
                    if (ctrl == null) continue;
                    var m = CtrlName.Match(cname);
                    if (!m.Success) m = CtrlName.Match(a.name.ToUpperInvariant());
                    if (!m.Success) continue;
                    if (!a.isHuman) continue;
                    string rawId = m.Groups[1].Value;
                    // костюмы (C1560 — купальник) используют анимации базового персонажа: суффикс C1500
                    string id = null; int idle = 0;
                    foreach (var cand in new[] { rawId, rawId.Substring(0, 3) + "00", rawId.Substring(0, 4) + "0" })
                    {
                        int h = Animator.StringToHash("10011_DG_Idle_" + cand);
                        if (!a.HasState(0, h)) h = Animator.StringToHash("10011_DG_Idle_Start_" + cand);
                        if (a.HasState(0, h)) { id = cand; idle = h; break; }
                    }
                    if (id == null) { if (seen.Add("nostate|" + rawId)) Plugin.L.LogInfo($"Анимации: у {rawId} нет состояния idle"); continue; }
                    Transform root = a.transform;
                    try { var c = a.GetComponentInParent(Il2CppType.Of<CharacterController>(), false); if (c != null) root = c.Cast<Component>().transform; }
                    catch { if (a.transform.parent != null) root = a.transform.parent; }
                    var u = new Unit { anim = a, root = root, id = id, idle = idle, stnby = a.HasState(0, Animator.StringToHash("10021_DG_StnbyMotion_Start_" + id)) ? Animator.StringToHash("10021_DG_StnbyMotion_Start_" + id) : Animator.StringToHash("10021_DG_StnbyMotion_" + id), nextAt = Next() };
                    for (int i = 0; i < Gestures.Length; i++)
                    {
                        var hashes = new int[Gestures[i].Length + 1];
                        bool ok = true;
                        for (int j = 0; j < Gestures[i].Length; j++)
                        {
                            string nm = Gestures[i][j];
                            hashes[j] = Animator.StringToHash(nm + "_" + id);
                            if (!a.HasState(0, hashes[j]) && nm.EndsWith("_Start")) hashes[j] = Animator.StringToHash(nm.Substring(0, nm.Length - 6) + "_" + id);
                            if (!a.HasState(0, hashes[j])) { ok = false; break; }
                        }
                        if (!ok) continue;
                        hashes[^1] = (int)(GestureWeight[i] * 100);
                        u.gestures.Add(hashes);
                        var ids = new int[Gestures[i].Length + 1];
                        for (int j = 0; j < Gestures[i].Length; j++) ids[j] = int.Parse(Gestures[i][j].Substring(0, 5));
                        ids[^1] = hashes[^1];
                        u.ids.Add(ids);
                    }
                    foreach (var cn in CustomIdles.Carriers)
                    {
                        int h = Animator.StringToHash(cn + "_" + id);
                        if (!a.HasState(0, h)) continue;
                        foreach (var c in ctrl.animationClips) if (c != null && c.name.StartsWith(cn)) { u.carrierClip = c; u.carrierHash = h; break; }
                        if (u.carrierClip != null) break;
                    }
                    if (a.isHuman)
                    {
                        u.hips = a.GetBoneTransform(HumanBodyBones.Hips);
                        u.head = a.GetBoneTransform(HumanBodyBones.Head); u.neck = a.GetBoneTransform(HumanBodyBones.Neck);
                        u.chest = a.GetBoneTransform(HumanBodyBones.UpperChest) ?? a.GetBoneTransform(HumanBodyBones.Chest);
                        u.spine = a.GetBoneTransform(HumanBodyBones.Spine);
                        u.eyeL = a.GetBoneTransform(HumanBodyBones.LeftEye); u.eyeR = a.GetBoneTransform(HumanBodyBones.RightEye);
                    }
                    try { var c2 = root.GetComponent(Il2CppType.Of<MapUnitBaseComponent>()); if (c2 != null) u.mub = c2.TryCast<MapUnitBaseComponent>(); } catch { }
                    if (u.mub != null && seen.Add("idlecfg|" + a.name))
                        Plugin.L.LogInfo($"Анимации: {a.name} idle игры: ожидание {u.mub.animation_idle_wait_time_:0.0}+{u.mub.animation_idle_wait_time_random_:0.0} с, доля idle {u.mub.animation_idle_rate_idle_:0.00}, standby {u.mub.animation_idle_rate_standby_:0.00}");
                    u.breathPhase = (float)rnd.NextDouble() * 6.28f;
                    u.lastPos = a.transform.position;
                    units[a.Pointer] = u;
                    Last = $"{id}: жестов {u.gestures.Count}, кости головы {(u.head != null ? "есть" : "нет")}";
                    Plugin.L.LogInfo($"Анимации: найдена {a.name} (анимации {id}) '{a.name}', корень '{root.name}', жестов {u.gestures.Count}, human {a.isHuman}");
                }
            }
            catch (Exception e) { Last = "ошибка: " + e.Message; Plugin.L.LogWarning("Анимации: " + e); }
        }

        /// После анимации, до физики волос: дыхание и взгляд.
        public static int LateTicks;
        public static void LateUpdate()
        {
            LateTicks++;
            if (!S.IdleBreath.Value && !S.IdleLook.Value) return;
            var cam = Camera.main;
            float dt = Time.unscaledDeltaTime, t = Time.unscaledTime;
            foreach (var u in units.Values)
            {
                try
                {
                    if (u.anim == null) continue;
                    var st = u.anim.GetCurrentAnimatorStateInfo(0);
                    bool calm = st.m_Name == u.idle || st.m_Name == u.stnby || u.seq != null || u.idSeq != null;

                    if (S.IdleBreath.Value && u.chest != null)
                    {
                        // вдох около 3,6 с, грудная клетка и плечи чуть поднимаются
                        float b = (float)Math.Sin(t * 1.75f + u.breathPhase);
                        float amp = S.IdleBreathAmount.Value * (calm ? 1f : 0.35f);
                        u.chest.localRotation = u.chest.localRotation * Quaternion.Euler(-b * 1.6f * amp, 0, 0);
                        if (u.spine != null) u.spine.localRotation = u.spine.localRotation * Quaternion.Euler(b * 0.6f * amp, 0, 0);
                    }

                    if (S.IdleLook.Value && u.head != null && cam != null)
                    {
                        var target = cam.transform.position;
                        var fwd = u.root.forward; fwd.y = 0;
                        var to = target - u.head.position;
                        float dist = to.magnitude;
                        var toFlat = new Vector3(to.x, 0, to.z);
                        float ang = Vector3.Angle(fwd, toFlat);
                        float wantW = (calm && u.seq == null && u.idSeq == null && dist < S.IdleLookDistance.Value && ang < 95f) ? 1f : 0f;
                        u.lookW += (wantW - u.lookW) * (1f - (float)Math.Exp(-dt / 0.35f));
                        if (u.lookW > 0.001f)
                        {
                            // поворот, ограниченный по горизонтали и вертикали
                            var desired = Quaternion.FromToRotation(fwd.normalized, to.normalized);
                            float w = Math.Clamp(Math.Abs(desired.w), 0f, 1f);
                            float a = 2f * (float)Math.Acos(w) * 57.29578f;
                            float f = a > 50f ? 50f / a : 1f;
                            var delta = Quaternion.Slerp(Quaternion.identity, desired, f * u.lookW);
                            u.lookDelta = Quaternion.Slerp(u.lookDelta, delta, 1f - (float)Math.Exp(-dt / 0.15f));
                            if (u.neck != null)
                            {
                                var half = Quaternion.Slerp(Quaternion.identity, u.lookDelta, 0.35f);
                                u.neck.rotation = half * u.neck.rotation;
                                u.head.rotation = Quaternion.Slerp(Quaternion.identity, u.lookDelta, 0.65f) * u.head.rotation;
                            }
                            else u.head.rotation = u.lookDelta * u.head.rotation;
                            if (u.eyeL != null) u.eyeL.rotation = Quaternion.Slerp(Quaternion.identity, u.lookDelta, 0.3f) * u.eyeL.rotation;
                            if (u.eyeR != null) u.eyeR.rotation = Quaternion.Slerp(Quaternion.identity, u.lookDelta, 0.3f) * u.eyeR.rotation;
                        }
                        else u.lookDelta = Quaternion.identity;
                    }
                }
                catch { }
            }
        }
    }
}
