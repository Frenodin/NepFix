using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace NepFix
{
    /// Бой. Таймеры боя в коде игры считаются от реального времени, но игра рассчитана на 60 кадров. Если бой на высоком FPS
    /// кажется несправедливым, «Лимит FPS в бою» ограничивает FPS только на время боя (по умолчанию выключен).
    /// В лог пишется итог каждого боя: удары и урон по сторонам, действия врагов, средний FPS.
    internal static class BattleWatch
    {
        static Settings S => Plugin.S;
        public static bool Active { get; private set; }
        static float pollT = -1;

        // ---- замер боя ----
        static float startT, fpsSum; static int fpsN, startFrame;
        static int hitsToEnemy, hitsToParty, actions;
        static long dmgToEnemy, dmgToParty, guardToEnemy;
        static readonly Dictionary<string, int> actionTypes = new();
        static bool boss, lose;

        public static bool CapActive => Active && S.BattleFpsCap.Value > 0;

        /// Раз в кадр из Update мода, сам опрос не чаще 10 раз в секунду.
        public static void Tick()
        {
            float now = Time.unscaledTime;
            if (Active) { float dt = Time.unscaledDeltaTime; if (dt > 0) { fpsSum += 1f / dt; fpsN++; } }
            if (Active && now - sumT > 60f) { sumT = now; Summary("Бой, промежуточно"); }
            if (now - pollT < 0.1f) return;
            pollT = now;
            bool b = false;
            try { b = BattleSystem.IsBattle(); } catch { }
            if (b == Active) return;
            Active = b;
            if (b) Begin(); else End();
            Gfx.EnforceFps();
        }

        static void Begin()
        {
            startT = sumT = Time.unscaledTime; fpsSum = 0; fpsN = 0; startFrame = Time.frameCount;
            hitsToEnemy = hitsToParty = actions = 0; dmgToEnemy = dmgToParty = guardToEnemy = 0;
            actionTypes.Clear(); lastAction.Clear();
            try { boss = BattleSystem.IsBossBattle(); } catch { boss = false; }
            try { lose = BattleSystem.IsLoseEventBattle(); } catch { lose = false; }
            Plugin.L.LogInfo($"Бой начался: босс {(boss ? "да" : "нет")}, сюжетный проигрыш {(lose ? "да" : "нет")}, " +
                             (S.BattleFpsCap.Value > 0 ? $"FPS в бою ограничен {S.BattleFpsCap.Value}" : $"FPS в бою без ограничения, цель {Gfx.TargetFps}"));
        }

        static void End() => Summary("Бой окончен");

        static float sumT;
        static string CapText => S.BattleFpsCap.Value > 0 ? S.BattleFpsCap.Value.ToString() : "нет";
        static void Summary(string head)
        {
            float dur = Math.Max(0.01f, Time.unscaledTime - startT);
            float fps = (Time.frameCount - startFrame) / dur;
            var sb = new StringBuilder();
            sb.Append($"{head}: {dur:0} с, средний FPS {fps:0}, лимит в бою {CapText}. ");
            sb.Append($"Удары по врагам: {hitsToEnemy} ({hitsToEnemy / dur * 60:0.0} в мин), урон {dmgToEnemy}, в среднем {(hitsToEnemy > 0 ? dmgToEnemy / hitsToEnemy : 0)}, по защите {guardToEnemy}. ");
            sb.Append($"Удары по отряду: {hitsToParty} ({hitsToParty / dur * 60:0.0} в мин), урон {dmgToParty}, в среднем {(hitsToParty > 0 ? dmgToParty / hitsToParty : 0)}. ");
            sb.Append($"Действий врагов: {actions} ({actions / dur * 60:0.0} в мин):");
            foreach (var kv in actionTypes) sb.Append($" {kv.Key}={kv.Value}");
            Plugin.L.LogInfo(sb.ToString());
        }

        public static void OnDamage(RpgUnit user, RpgUnit target, DbCalculateCharaResult r, bool toEnemy)
        {
            if (!Active || r == null || target == null) return;
            try
            {
                int hp = Math.Abs(r.hp_); int guard = r.guard_damage_;
                if (toEnemy) { hitsToEnemy++; dmgToEnemy += hp; guardToEnemy += guard; }
                else { hitsToParty++; dmgToParty += hp; }
            }
            catch { }
        }

        static readonly Dictionary<IntPtr, int> lastAction = new();
        public static void OnEnemyAction(IntPtr ai, DefDatabase.ActionType type)
        {
            if (!Active) return;
            int v = (int)type;
            if (lastAction.TryGetValue(ai, out int prev) && prev == v) return;
            lastAction[ai] = v;
            actions++;
            string k = type.ToString();
            actionTypes.TryGetValue(k, out int n); actionTypes[k] = n + 1;
        }
    }

    // Урон по отряду и по врагам: у BattleAttackCollisionControl отдельные методы для каждой стороны.
    [HarmonyPatch(typeof(BattleAttackCollisionControl), "DamagePlayer")]
    internal static class PatchBattleDamage
    {
        static void Postfix(BattleAttackCollisionControl __instance, DbCalculateCharaResult result_calculation, RpgUnit hit_rpg)
        { RpgUnit u = null; try { u = __instance?.user_unit_; } catch { } BattleWatch.OnDamage(u, hit_rpg, result_calculation, false); }
    }

    [HarmonyPatch(typeof(BattleAttackCollisionControl), "DamageEnemy")]
    internal static class PatchBattleDamageEnemy
    {
        static void Postfix(BattleAttackCollisionControl __instance, DbCalculateCharaResult result_calculation, RpgUnit hit_rpg)
        { RpgUnit u = null; try { u = __instance?.user_unit_; } catch { } BattleWatch.OnDamage(u, hit_rpg, result_calculation, true); }
    }

    [HarmonyPatch(typeof(Battle.BattleAI.AIBase.AIBattleBaseEnemy.AIBattleBaseEnemy), "SetActionType")]
    internal static class PatchEnemyAction
    {
        static void Postfix(Battle.BattleAI.AIBase.AIBattleBaseEnemy.AIBattleBaseEnemy __instance, DefDatabase.ActionType type) { if (__instance != null) BattleWatch.OnEnemyAction(__instance.Pointer, type); }
    }
}
