using System.Collections;
using UnityEngine;

namespace Apl.Client.Match
{
    /// <summary>
    /// Fireworks over the stadium for the winning moment (owner's list, item 22),
    /// all in code: each shell is a particle burst high over the stands (a sphere
    /// of sparks that fall and fade, a few glittering trails), a quick point-light
    /// flash on the crowd, and a boom a beat later (sound takes time). A
    /// <see cref="Show"/> fires a volley round the ground and lifts the sky.
    /// </summary>
    public sealed class Fireworks : MonoBehaviour
    {
        private static readonly Color[] Colours =
        {
            new Color(1f, 0.82f, 0.3f), new Color(1f, 0.35f, 0.3f), new Color(0.45f, 0.75f, 1f),
            new Color(0.75f, 0.45f, 1f), new Color(0.5f, 1f, 0.55f), Color.white,
        };

        private Material _spark = null!;
        private AudioSource _audio = null!;
        private AudioClip _boom = null!;
        private Light? _sky;

        public static Fireworks Create()
        {
            var go = new GameObject("Fireworks");
            var f = go.AddComponent<Fireworks>();
            f._spark = new Material(Shader.Find("Sprites/Default")) { mainTexture = Dot() };
            f._audio = go.AddComponent<AudioSource>();
            f._boom = Boom();
            // The scene's sun, if there is one, for the sky lift.
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) f._sky = l;
            return f;
        }

        /// <summary>A volley of <paramref name="shells"/> round the ground over <paramref name="seconds"/>, the team's colour mixed in.</summary>
        public IEnumerator Show(int shells, float seconds, Color team)
        {
            StartCoroutine(SkyLift(seconds));
            for (var i = 0; i < shells; i++)
            {
                var a = Random.Range(0f, Mathf.PI * 2);
                var at = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * Random.Range(62f, 92f) + Vector3.up * Random.Range(34f, 58f);
                Burst(at, Random.value < 0.4f ? team : Colours[Random.Range(0, Colours.Length)]);
                yield return new WaitForSeconds(seconds / shells * Random.Range(0.5f, 1.5f));
            }
        }

        /// <summary>One shell bursting at <paramref name="at"/>.</summary>
        public void Burst(Vector3 at, Color c)
        {
            var go = new GameObject("Shell");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(11f, 17f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(c, Color.Lerp(c, Color.white, 0.5f));
            main.gravityModifier = 0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 220;
            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 160) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.4f;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0, 1) });
            fade.color = g;
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 1.6f;
            var trails = ps.trails;
            trails.enabled = true;
            trails.ratio = 0.3f;
            trails.lifetime = 0.25f;
            trails.widthOverTrail = 0.35f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = _spark;
            r.trailMaterial = _spark;
            ps.Play();
            // The flash on the crowd below.
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = c;
            light.range = 70f;
            StartCoroutine(Fade(light, 3.5f, 0.5f));
            StartCoroutine(BoomLater(Vector3.Distance(at, Vector3.zero) / 340f));
            Destroy(go, 3f);
        }

        private IEnumerator BoomLater(float delay)
        {
            yield return new WaitForSeconds(delay);
            _audio.PlayOneShot(_boom, Random.Range(0.35f, 0.6f));
        }

        private static IEnumerator Fade(Light l, float from, float seconds)
        {
            for (var t = 0f; t < 1f && l != null; t += Time.deltaTime / seconds)
            {
                l.intensity = from * (1 - t);
                yield return null;
            }
        }

        /// <summary>The sky lifts for the show: the sun brightens a touch and settles back.</summary>
        private IEnumerator SkyLift(float seconds)
        {
            if (_sky == null) yield break;
            var base0 = _sky.intensity;
            for (var t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                _sky.intensity = base0 * (1f + 0.35f * Mathf.Sin(t * Mathf.PI) + 0.1f * Mathf.PerlinNoise(t * 20f, 0));
                yield return null;
            }
            _sky.intensity = base0;
        }

        /// <summary>A soft round spark.</summary>
        private static Texture2D Dot()
        {
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1 - d) * Mathf.Clamp01(1 - d)));
            }
            tex.Apply();
            return tex;
        }

        /// <summary>A distant boom: a low thump with a crackling noise tail.</summary>
        private static AudioClip Boom()
        {
            const int rate = 22050;
            var n = (int)(rate * 1.4f);
            var data = new float[n];
            var rng = new System.Random(3);
            float low = 0;
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)rate;
                var noise = (float)(rng.NextDouble() * 2 - 1);
                low += (noise - low) * 0.06f;
                data[i] = Mathf.Clamp(Mathf.Exp(-t * 5f) * (2.2f * low + 0.6f * Mathf.Sin(2 * Mathf.PI * 48f * t)) + (t > 0.35f ? noise * 0.08f * Mathf.Exp(-(t - 0.35f) * 4f) * (rng.NextDouble() < 0.02 ? 6 : 1) : 0), -1, 1);
            }
            var clip = AudioClip.Create("firework_boom", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
