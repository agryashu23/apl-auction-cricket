using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>Confetti: squares in the winners' colours (and gold, white) fluttering down over the square for a few seconds.</summary>
    public static class Confetti
    {
        public static void Drop(Color a, Color b)
        {
            var go = new GameObject("Confetti");
            go.transform.position = new Vector3(0, 17f, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 4f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 8.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.12f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.gravityModifier = 0.06f;
            main.maxParticles = 480;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var gold = new Color(1f, 0.8f, 0.25f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Gradient
            {
                mode = GradientMode.Fixed,
                colorKeys = new[] { new GradientColorKey(a, 0.4f), new GradientColorKey(b, 0.65f), new GradientColorKey(gold, 0.85f), new GradientColorKey(Color.white, 1f) },
            }) { mode = ParticleSystemGradientMode.RandomColor };
            var emission = ps.emission;
            emission.rateOverTime = 60;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 220) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(40f, 1f, 40f);
            // Flutter: a little turbulence and spin as they fall.
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.8f;
            noise.frequency = 0.4f;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-4f, 4f);
            rot.y = new ParticleSystem.MinMaxCurve(-4f, 4f);
            rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.alignment = ParticleSystemRenderSpace.Local;
            r.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = Texture2D.whiteTexture };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            Object.Destroy(go, 14f);
        }
    }
}
