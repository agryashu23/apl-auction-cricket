using System;
using System.Linq;
using Apl.Core.Play;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Run-out owner (2026-11-13; owner: "in real play I don't see run outs"). The PM's evidence path for what the
    /// owner sees, not a forced loop: <c>-aplmatchtest -aplrunoutreal</c> plays a normal FULL match (the toss, both
    /// innings: the user bats one and bowls one, as the owner plays it) where both sides run exactly as in real play
    /// (user AUTO: the safe runs, never run out; the AI: its own judgement; no test nudges) and the trace
    /// (MatchDirector.RunOutTrace) logs every chance and an innings summary with the side and the running mode.
    /// Add <c>-aplrunmanual</c> to drive the user's side through MANUAL running (the owner's default setting): a
    /// scripted player presses RUN for the runs on offer, now and then one more when the HUD's risk reads low,
    /// sometimes calls a beat late, presses DIVE on the final approach when it's lit, and calls the overthrow's run
    /// when a shy runs away. The chips' timing is logged ("[APL runbtn] shown … / hidden … (why)").
    /// </summary>
    public sealed partial class MatchDirector
    {
        private bool? _runOutReal, _runManualBot;

        /// <summary>-aplrunoutreal (test runs): real running for both sides, nothing forced.</summary>
        private bool RunOutReal => _runOutReal ??= Autoplay && Environment.GetCommandLineArgs().Contains("-aplrunoutreal");

        /// <summary>-aplrunmanual (test runs): the user's side runs MANUAL, called by the scripted player.</summary>
        private bool RunManualBot => _runManualBot ??= Autoplay && Environment.GetCommandLineArgs().Contains("-aplrunmanual");

        /// <summary>Which side's running this is, for the trace.</summary>
        private string RunSideLabel => _match.UserBatting
            ? (RunState.Manual ? "user MANUAL" : "user AUTO")
            : "AI";

        /// <summary>The scripted player's plan for this ball: how many runs he'll call, when he starts pressing.</summary>
        private int _botWant;
        private float _botPressAt;
        private bool _botDive;

        /// <summary>At the start of a MANUAL ball (RunBetween): what the scripted player will call.</summary>
        private void BotPlan(RunPlan plan)
        {
            _botWant = plan.Safe;
            // The extra one when the HUD says it's on (a low risk), about half the time.
            if (_botWant < 3 && plan.Risk(_botWant + 1) < 0.35f && UnityEngine.Random.value < 0.5f) _botWant++;
            // Most calls in the first 0.1–0.3 s; one in five a beat late.
            _botPressAt = UnityEngine.Random.value < 0.2f ? UnityEngine.Random.Range(0.4f, 0.7f) : UnityEngine.Random.Range(0.1f, 0.3f);
            _botDive = UnityEngine.Random.value < 0.3f;
            if (_botWant > 0) Debug.Log($"[APL runout] manual bot: calls {_botWant} (safe {plan.Safe}, risk of {_botWant} = {plan.Risk(_botWant):0.00}) at +{_botPressAt:0.00} s");
        }

        /// <summary>The scripted player sees the overthrow run away and calls it, a beat after (0.2–0.6 s), nine times in ten.</summary>
        private bool BotTakesOverthrow(float standing)
        {
            if (standing < 0.01f) _botOverthrowAt = UnityEngine.Random.value < 0.9f ? UnityEngine.Random.Range(0.2f, 0.6f) : float.MaxValue;
            return standing >= _botOverthrowAt;
        }

        private float _botOverthrowAt = float.MaxValue;

        /// <summary>Each frame of a MANUAL ball: the scripted player's presses (one RUN a frame until he has his runs).</summary>
        private void BotPress(float since, int target, bool diveLit, bool diveMatters, ref bool run, ref bool dive)
        {
            if (since >= _botPressAt && target < _botWant) run = true;
            if (diveLit && (diveMatters || _botDive)) dive = true;
        }
    }
}
