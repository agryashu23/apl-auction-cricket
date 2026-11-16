using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Apl.Client.Hall
{
    /// <summary>Animator triggers shared by every controller (AnimatorBuilder creates them).</summary>
    public static class Anim
    {
        public static readonly int Talk = Animator.StringToHash("Talk");
        public static readonly int Point = Animator.StringToHash("Point");
        public static readonly int Raise = Animator.StringToHash("Raise");
        public static readonly int Clap = Animator.StringToHash("Clap");
        public static readonly int ThumbsUp = Animator.StringToHash("ThumbsUp");
        public static readonly int Disbelief = Animator.StringToHash("Disbelief");
        public static readonly int Laugh = Animator.StringToHash("Laugh");
        public static readonly int Pick = Animator.StringToHash("Pick");
        public static readonly int Variant = Animator.StringToHash("Variant");
        public static readonly int Talk2 = Animator.StringToHash("Talk2");
        public static readonly int No = Animator.StringToHash("No");
        public static readonly int RaiseNo = Animator.StringToHash("RaiseNo");
        public static readonly int FistPump = Animator.StringToHash("FistPump");
        public static readonly int NodYes = Animator.StringToHash("NodYes");
        public static readonly int Discuss = Animator.StringToHash("Discuss");
        public static readonly int Typing = Animator.StringToHash("Typing");
        public static readonly int TalkLeft = Animator.StringToHash("TalkLeft");
        public static readonly int TalkRight = Animator.StringToHash("TalkRight");
    }
}
