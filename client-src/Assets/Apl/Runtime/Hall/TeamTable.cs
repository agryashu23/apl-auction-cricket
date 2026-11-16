using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Hall
{
    /// <summary>One franchise's table: its people, the bidding paddle, the flag.</summary>
    public sealed class TeamTable : MonoBehaviour
    {
        public string TeamId = "";
        public List<SeatedMember> Members = new List<SeatedMember>();

        /// <summary>The member who holds the paddle (seat nearest the stage).</summary>
        public SeatedMember? Bidder;

        /// <summary>The member with the laptop, who types on it now and then.</summary>
        public SeatedMember? Typist;
        private float _nextTyping;
        public Renderer? Highlight;

        private bool _highlighted;
        /// <summary>The ring's own material (the scene's is gold), recoloured every frame.</summary>
        private Material? _ring;

        private void Update()
        {
            if (_highlighted && _ring != null)
            {
                // Your table's floor ring pulses white ↔ lime (the YOU colours; gold read as Chennai's).
                var c = Apl.Client.Ui.YouMark.Pulse(Time.unscaledTime);
                _ring.SetColor("_BaseColor", c);
                _ring.SetColor("_EmissionColor", c * 2.2f);
            }
            // Between moments, the analyst at the laptop works on it.
            if (Typist == null || Time.time < _nextTyping) return;
            _nextTyping = Time.time + Random.Range(6f, 16f);
            if (Random.value < 0.6f) Typist.Type(Random.Range(2.5f, 5f));
        }

        private void Start()
        {
            _nextTyping = Time.time + Random.Range(1f, 8f);
            // SetHighlight may already have run (Start order across objects is undefined).
            if (Highlight != null) Highlight.enabled = _highlighted;
        }

        public void SetHighlight(bool on)
        {
            _highlighted = on;
            if (Highlight == null) return;
            Highlight.enabled = on;
            if (on && _ring == null)
            {
                _ring = Highlight.material;
                _ring.EnableKeyword("_EMISSION");
            }
        }

        public void Bid()
        {
            Bidder?.RaisePaddle();
            // A teammate reacts now and then, as people do.
            if (Members.Count > 1 && Random.value < 0.4f)
            {
                var other = Members[Random.Range(0, Members.Count)];
                if (other != Bidder) other.React(Random.value < 0.5f ? Anim.Talk : Anim.Talk2);
            }
        }

        /// <summary>Got him: two neighbours high-five, the third pumps a fist or claps.</summary>
        public void Won()
        {
            var pair = Neighbours();
            if (pair != null)
            {
                var (a, b) = pair.Value;
                // Hands meet above the table between their shoulders.
                var mid = (a.transform.position + b.transform.position) / 2f + Vector3.up * 1.45f + (a.transform.forward + b.transform.forward) * 0.18f;
                var bOnARight = Vector3.Dot(b.transform.position - a.transform.position, a.transform.right) > 0;
                a.LookAt(b.transform, 1.6f);
                b.LookAt(a.transform, 1.6f);
                a.HighFive(mid, rightHand: bOnARight, delay: 0.3f);
                b.HighFive(mid, rightHand: !bOnARight, delay: 0.3f);
            }
            foreach (var m in Members)
            {
                if (pair != null && (m == pair.Value.a || m == pair.Value.b)) continue;
                m.React(Random.value < 0.5f && m.Has(Anim.FistPump) ? Anim.FistPump : Random.value < 0.6f ? Anim.Clap : Anim.ThumbsUp);
            }
        }

        /// <summary>
        /// A quick word between two of them: one talks (turned to the other),
        /// the other listens and nods. Used between bids, before some bids and
        /// over an RTM decision.
        /// </summary>
        public void Confer(float seconds = 2.2f)
        {
            var pair = Neighbours();
            if (pair == null) return;
            var (a, b) = Random.value < 0.5f ? pair.Value : (pair.Value.b, pair.Value.a);
            a.LookAt(b.transform, seconds);
            b.LookAt(a.transform, seconds);
            // Turn-and-talk towards the listener when the clip is there; a longer
            // huddle (RTM) leans in to discuss.
            var listenerOnLeft = Vector3.Dot(b.transform.position - a.transform.position, a.transform.right) < 0;
            var turn = listenerOnLeft ? Anim.TalkLeft : Anim.TalkRight;
            if (seconds >= 4f && a.Has(Anim.Discuss)) a.React(Anim.Discuss);
            else if (a.Has(turn)) a.React(turn);
            else a.React(Random.value < 0.5f ? Anim.Talk : Anim.Talk2);
            StartCoroutine(NodLater(b, Mathf.Min(1.2f, seconds * 0.5f)));
        }

        private static IEnumerator NodLater(SeatedMember m, float delay)
        {
            yield return new WaitForSeconds(delay);
            m.Nod();
        }

        /// <summary>Two adjacent seats (members are in seat order), or null.</summary>
        private (SeatedMember a, SeatedMember b)? Neighbours()
        {
            if (Members.Count < 2) return null;
            var i = Random.Range(0, Members.Count - 1);
            return (Members[i], Members[i + 1]);
        }

        /// <summary>Right to Match used: up goes the RTM card, the table celebrates.</summary>
        public void UseRtm()
        {
            Bidder?.RaiseRtm();
            foreach (var m in Members)
            {
                if (m != Bidder) m.React(Random.value < 0.5f ? Anim.Clap : Anim.ThumbsUp);
            }
        }

        /// <summary>Right to Match declined: a shake of the head from the card holder.</summary>
        public void DeclineRtm()
        {
            Bidder?.SayNo();
            if (Members.Count > 1)
            {
                var other = Members[Random.Range(0, Members.Count)];
                if (other != Bidder) other.Disapprove();
            }
        }

        public void LostOut()
        {
            if (Members.Count == 0) return;
            var r = Random.value;
            Members[Random.Range(0, Members.Count)].React(r < 0.4f ? Anim.Disbelief : r < 0.7f ? Anim.No : Anim.Laugh);
        }
    }
}
