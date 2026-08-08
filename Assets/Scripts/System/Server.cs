using System.Collections.Generic;
using UnityEngine;

namespace Server
{
    public static class UnitMotion
    {
        public static void MoveThisObject(UnitAttribute a, GameObject o, Vector2 dir)
        {
            if (o == null) return;

            float speed = a.SPEED_Current;
            Vector2 step = speed * dir * Time.deltaTime;
            o.transform.position += new Vector3(step.x, step.y, 0);
            
        }

        public static void RotateThisObject(GameObject o, Vector2 dir,float offset = 90)
        {
            if (o == null) return;

            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - offset;
            o.transform.rotation = Quaternion.Euler(0, 0, angle);
        }
        public static Vector2 GetSeparationForce(Vector2 selfPos, List<Zombrain> nearby, float pushRadius, float pushStrength)
        {
            Vector2 push = Vector2.zero;
            int count = 0;

            foreach (var other in nearby)
            {
                if (other == null) continue;
                Vector2 otherPos = other.transform.position;
                Vector2 offset = selfPos - otherPos;
                float dist = offset.magnitude;

                if (dist > 0f && dist < pushRadius)
                {
                    push += offset.normalized * (1f - dist / pushRadius); // stronger push when closer
                    count++;
                }
            }

            if (count > 0) push /= count;
            return push * pushStrength;
        }
    }

    public static class PrimaryInput
    {
        public static Vector2 GetInput()
        {
            float x = Input.GetAxis("Horizontal");
            float y = Input.GetAxis("Vertical");
            Vector2 final = new Vector2(x, y);
            return final;

            //fix later
        }
    }
    public static class Calculation
    {
        public static float FinalValue(float Base, Amplification ampl)
        {
            return ampl.TotalBonus * (Base + ampl.FlatBonus * ampl.PercBonus);
        }
    }

    public static class Combat
    {
        public static void DealDamage(float damage, UnitAttribute attacker = default, UnitAttribute victim = default)
        {
            if (attacker == null || victim == null) return;
            if (victim.IsDead) return;

            damage = HandleDamage(damage, attacker, victim);
            victim.TakeDamage(damage);

            victim.OnTakeDamage?.Invoke(damage);
            attacker.OnDealDamage?.Invoke(damage);
        }
        public static float HandleDamage(float damage,UnitAttribute a, UnitAttribute v)
        {
            float effectiveDamage = damage * (1 + a.DealtDamage_Extra);
            float effectiveArmour = v.DEF_Current * (1 - a.ArmourPenetration_Perc);

            if (effectiveArmour <= 0) return effectiveDamage;
            return damage * (500 / (effectiveArmour + 500));
        }
    }

    public static class RNG
    {
        private static Unity.Mathematics.Random _state;
        private static uint _seed;

        static RNG() => SetSeed((uint)System.DateTime.Now.Ticks);

        public static void SetSeed(uint seed)
        {
            _seed = seed == 0 ? 1u : seed;
            _state = new Unity.Mathematics.Random(_seed);
        }

        public static void Reset() => _state = new Unity.Mathematics.Random(_seed);
        public static uint Seed => _seed;

        public static int GetInt(int min, int max) => _state.NextInt(min, max);
        public static float GetFloat(float min, float max) => _state.NextFloat(min, max);
        public static float GetPercent() => GetFloat(0,100f) / 100f;
        public static Vector2 GetVector2(float min, float max) => _state.NextFloat2(min, max);
        public static Vector2 GetInsideCircle(float radius)
        {
            float angle = GetFloat(0, Mathf.PI * 2f);
            float dist = GetFloat(0, radius);
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
        }
    }

}
