using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Runs against real CharacterControllers and scene geometry in the licensed editor.
[InitializeOnLoad]
public static class BotPlayChecks
{
    const string Pending = "FloatingTiles.BotChecks";
    static IEnumerator suite;
    static float resumeAt;
    static int passed, failed;
    static readonly string ReportPath = "Logs/bot-play-checks.txt";
    static readonly Dictionary<TileActor, Vector3> originalPositions = new Dictionary<TileActor, Vector3>();
    static BotPlayChecks() { EditorApplication.update += Update; }

    [MenuItem("Floating Tiles/Run Bot Play Checks")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("Stop Play mode before running bot checks."); return; }
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, "Bot v0.1 integration checks\n");
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
    static void Update()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (suite == null)
            {
                var map = UnityEngine.Object.FindFirstObjectByType<FloatingMap>();
                if (!map || map.Navigation == null) return;
                passed = failed = 0;
                originalPositions.Clear();
                foreach (var actor in map.Actors) originalPositions[actor] = actor.transform.position;
                map.StopAllCoroutines();
                if (map.player) map.player.enabled = false;
                Application.runInBackground = true;
                suite = Checks(map);
                resumeAt = 0;
            }
            if (Time.time < resumeAt) return;
            if (suite.MoveNext()) resumeAt = Time.time + Convert.ToSingle(suite.Current);
            else Complete();
        }
        catch (Exception exception)
        {
            Check(false, exception.ToString());
            Complete();
        }
    }
    static void Complete()
    {
        File.AppendAllText(ReportPath, $"RESULT: {passed} passed, {failed} failed\n");
        Debug.Log($"BOT_CHECKS_COMPLETE: {passed} passed, {failed} failed. {ReportPath}");
        SessionState.SetBool(Pending, false);
        suite = null;
        EditorApplication.ExitPlaymode();
    }
    static void Check(bool value, string message)
    {
        if (value) passed++; else failed++;
        File.AppendAllText(ReportPath, (value ? "PASS: " : "FAIL: ") + message + "\n");
    }
    static void Prepare(FloatingMap map, params TileActor[] active)
    {
        map.StopAllCoroutines();
        foreach (var actor in map.Actors)
        {
            actor.gameObject.SetActive(true);
            actor.ResetRound(originalPositions[actor]);
        }
        foreach (var bot in map.Bots) bot.enabled = false;
        map.BeginRound();
        if (active.Length > 0) foreach (var actor in map.Actors)
            actor.gameObject.SetActive(Array.IndexOf(active, actor) >= 0);
    }
    static Vector3 Above(Renderer tile) => new Vector3(tile.bounds.center.x,
        tile.bounds.max.y + TileActor.Height * .5f + .025f, tile.bounds.center.z);
    static void Assign(TileActor actor, Renderer target, int colour = 0)
    {
        Vector3 destination = target.bounds.center; destination.y = target.bounds.max.y;
        actor.Assign(colour, target, destination, FloatingMap.Colours[colour]);
    }
    static IEnumerator Checks(FloatingMap map)
    {
        Check(map.Bots.Count == 7 && map.Actors.Count == 8, "Scene starts one human and seven bots");
        Check(map.Navigation.NodeCount > 100, "Navigation builds from scene terrain");
        var bot = map.Bots[0];
        var runner = bot.GetComponent<TileActor>();
        var human = map.Human;
        Prepare(map);
        foreach (var item in map.Bots) item.enabled = true;
        yield return .5f;
        bool waited = true;
        foreach (var item in map.Bots)
        {
            var actor = item.GetComponent<TileActor>();
            Vector3 delta = actor.transform.position - originalPositions[actor]; delta.y = 0;
            waited &= delta.magnitude < .03f && actor.ColourIndex == -1;
        }
        Check(waited, "Bots do not anticipate targets before the reveal");
        float reaction = bot.personality.reactionDelay;
        bot.personality.reactionDelay = .7f;
        Vector3 beforeReaction = runner.transform.position;
        map.RevealRound();
        yield return .2f;
        Vector3 reactionDelta = runner.transform.position - beforeReaction; reactionDelta.y = 0;
        Check(reactionDelta.magnitude < .04f, "Reaction delay actually delays movement");
        bot.personality.reactionDelay = reaction;
        var counts = new int[4];
        bool slots = true;
        foreach (var a in map.Actors)
        {
            counts[a.ColourIndex]++;
            foreach (var b in map.Actors) if (a != b && a.ColourIndex == b.ColourIndex)
                slots &= a.Target == b.Target && Vector3.Distance(a.TargetPosition, b.TargetPosition) >= TileActor.Radius * 2;
        }
        Check(Array.TrueForAll(counts, count => count == 2) && slots, "Balanced colours share tiles with distinct, usable standing spots");
        Check(map.Bots[1].personality.assertiveness > bot.personality.assertiveness
            && map.Bots[2].personality.commitment < map.Bots[1].personality.commitment,
            "Planner, bulldozer and opportunist have distinct stable traits");

        Prepare(map, runner);
        runner.ResetRound(Above(map.tiles[0]));
        map.RevealRound();
        Assign(runner, map.tiles[24]);
        bot.ResetRound(); bot.Reveal(); bot.enabled = true;
        yield return 5f;
        Check(runner.IsOnTarget(), "Bot traverses the map and reaches its colour without traffic");
        Vector3 held = runner.transform.position;
        yield return .6f;
        Check(Vector3.Distance(held, runner.transform.position) < .08f && bot.State == "Holding", "Bot settles without jitter or continued advancing");

        Prepare(map, human, runner);
        map.RevealRound();
        human.ResetRound(Above(map.tiles[12]));
        runner.ResetRound(Above(map.tiles[11]));
        Assign(human, map.tiles[12]); Assign(runner, map.tiles[12]);
        bot.ResetRound(); bot.Reveal(); bot.enabled = true;
        Vector3 occupantPosition = human.transform.position;
        yield return 3f;
        Check(runner.IsOnTarget() && bot.State == "Holding" && Vector3.Distance(human.transform.position, occupantPosition) < .2f,
            "Bot chooses another safe spot when its anchor is occupied, without shoving the occupant away");

        Prepare(map, human, runner);
        float y = originalPositions[human].y;
        human.ResetRound(new Vector3(-1, y, -6.4f));
        runner.ResetRound(new Vector3(0, y, -6.4f));
        human.SetInput(Vector3.right);
        yield return .9f;
        float pushedRight = runner.transform.position.x;
        Check(pushedRight > .25f, "Human contact displaces a bot");
        human.ResetRound(new Vector3(-1, y, -6.4f));
        runner.ResetRound(new Vector3(0, y, -6.4f));
        runner.SetInput(Vector3.left);
        yield return .9f;
        float pushedLeft = -1 - human.transform.position.x;
        Check(pushedLeft > .25f && Mathf.Abs(pushedLeft - pushedRight) < .3f, "Bot contact displaces human with comparable strength");
        Check(human.MoveSpeed == runner.MoveSpeed && human.JumpHeightValue == runner.JumpHeightValue, "Movement limits are shared");

        Prepare(map, runner);
        runner.ResetRound(Above(map.tiles[0]));
        foreach (var tile in map.tiles) tile.gameObject.SetActive(tile == map.tiles[0] || tile == map.tiles[24]);
        foreach (var platform in map.platforms) platform.gameObject.SetActive(false);
        var islands = new TileNavigation(map);
        var path = new List<TileNavigation.Waypoint>();
        Check(!islands.FindPath(runner, map.tiles[24].transform.position, bot.personality, true, path),
            "Urgency does not create a route across an impossible gap");
        // Remove one 2m tile. This creates a feasible jump while the distant islands above do not.
        Prepare(map, runner);
        runner.ResetRound(Above(map.tiles[10]));
        foreach (var tile in map.tiles) tile.gameObject.SetActive(tile == map.tiles[10] || tile == map.tiles[12]);
        foreach (var platform in map.platforms) platform.gameObject.SetActive(false);
        var gap = new TileNavigation(map);
        Check(gap.FindPath(runner, map.tiles[12].transform.position, bot.personality, false, path)
            && path.Exists(point => point.jump), "A feasible gap is represented by an explicit jump link");
        Prepare(map, runner);
        map.moveSeconds = .2f;
        map.RevealRound();
        runner.ResetRound(Above(map.tiles[0]));
        Assign(runner, map.tiles[24]);
        bot.ResetRound(); bot.Reveal(); bot.enabled = true;
        int previousPushes = bot.PushDecisions;
        yield return 1f;
        Check(bot.PushDecisions > previousPushes, "A tight deadline triggers committed movement");

        map.moveSeconds = 6;
        int reached = 0;
        int avoidanceBefore = 0;
        foreach (var item in map.Bots) avoidanceBefore += item.AvoidanceDecisions;
        for (int round = 0; round < 3; round++)
        {
            Prepare(map);
            foreach (var item in map.Bots) item.enabled = true;
            map.RevealRound();
            map.StartCoroutine("Countdown", map.moveSeconds);
            yield return 6f;
            int safe = 0;
            foreach (var item in map.Bots)
            {
                var actor = item.GetComponent<TileActor>();
                if (actor.IsOnTarget()) safe++;
                File.AppendAllText(ReportPath, $"ROUND {round + 1}: {actor.name} {item.State} target={actor.ColourIndex} onTarget={actor.IsOnTarget()} position={actor.transform.position}\n");
            }
            reached += safe;
            Check(safe >= 5, $"Crowded round {round + 1}: {safe}/7 bots reach their own tile");
        }
        int avoidanceAfter = 0;
        foreach (var item in map.Bots) avoidanceAfter += item.AvoidanceDecisions;
        Check(avoidanceAfter > avoidanceBefore, "Crossing traffic produces avoidance decisions");

        Prepare(map);
        map.RevealRound();
        var target = human.Target;
        var wrong = runner.Target;
        foreach (var actor in map.Actors) if (actor.ColourIndex != human.ColourIndex) { wrong = actor.Target; break; }
        int humanColour = human.ColourIndex;
        human.ResetRound(Above(wrong) + Vector3.up * 1.5f);
        Assign(human, target, humanColour);
        map.DropBlackTiles();
        int visible = 0;
        foreach (var tile in map.tiles) if (tile.gameObject.activeSelf) visible++;
        Check(visible == 4 && !map.platforms[0].gameObject.activeSelf, "Deadline removes black tiles and the edge platforms");
        map.JudgeColours();
        Check(human.IsLaunched, "Jumping above a wrong colour still triggers the teammate's launch rule");
        float launchY = human.transform.position.y;
        yield return .3f;
        Check(human.transform.position.y > launchY + 4, "Wrong-colour launch retains the higher upward velocity");
        int lives = human.LivesRemaining;
        human.Die(); human.Die();
        Check(human.LivesRemaining == lives - 1, "Death removes exactly one life even when reported twice");
        var stableTraits = bot.personality;
        map.BeginRound();
        Check(!human.IsDead && human.LivesRemaining == lives - 1, "Next round respawns a player without restoring spent lives");
        Check(ReferenceEquals(stableTraits, bot.personality), "Personality persists across round resets");
        while (!human.IsEliminated) { human.Die(); if (!human.IsEliminated) human.ResetRound(originalPositions[human]); }
        human.ResetRound(originalPositions[human]);
        Check(human.IsDead && human.LivesRemaining == 0, "An eliminated player cannot respawn");
    }
}
