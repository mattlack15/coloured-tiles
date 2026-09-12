using System;
using UnityEngine;

public enum BotPreset { CarefulPlanner, Bulldozer, Opportunist }

[Serializable]
public class BotPersonality
{
    [Range(0, 1)] public float assertiveness;
    [Range(0, 1)] public float patience;
    [Range(0, 1)] public float riskTolerance;
    [Range(.05f, 1.2f)] public float reactionDelay;
    [Range(0, 1)] public float commitment;
    public static BotPersonality Create(BotPreset preset, System.Random random)
    {
        BotPersonality p;
        switch (preset)
        {
            case BotPreset.Bulldozer:
                p = new BotPersonality { assertiveness = .9f, patience = .18f, riskTolerance = .65f, reactionDelay = .24f, commitment = .85f };
                break;
            case BotPreset.Opportunist:
                p = new BotPersonality { assertiveness = .5f, patience = .35f, riskTolerance = .6f, reactionDelay = .14f, commitment = .2f };
                break;
            default:
                p = new BotPersonality { assertiveness = .2f, patience = .65f, riskTolerance = .12f, reactionDelay = .42f, commitment = .6f };
                break;
        }
        p.assertiveness = Mathf.Clamp01(p.assertiveness + Jitter(random));
        p.patience = Mathf.Clamp01(p.patience + Jitter(random));
        p.riskTolerance = Mathf.Clamp01(p.riskTolerance + Jitter(random));
        p.commitment = Mathf.Clamp01(p.commitment + Jitter(random));
        p.reactionDelay = Mathf.Clamp(p.reactionDelay + Jitter(random), .06f, 1.2f);
        return p;
    }
    static float Jitter(System.Random random) => ((float)random.NextDouble() - .5f) * .14f;
}
