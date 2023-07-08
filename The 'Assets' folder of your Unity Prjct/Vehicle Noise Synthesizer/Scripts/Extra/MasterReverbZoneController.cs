using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AroundTheGroundSimulator
{
    public class MasterReverbZoneController : MonoBehaviour
    {

        AudioSource[] aS;
        public List<AudioSource> simulated;
        public void Process()
        {
            List<Keyframe> ks2 = new List<Keyframe>()
            {
            new Keyframe(0,0.0f),
            new Keyframe(64,0.125f),
            new Keyframe(128,1f)
        };
            AnimationCurve aC2 = new AnimationCurve(ks2.ToArray());
            for (int i = 0; i < aC2.length; i++)
            {
                aC2.SmoothTangents(i, 0);
            }


            aS = GetAllAudioListeners(this.transform);
            for (int i = 0; i < aS.Length; i++)
            {
                aS[i].SetCustomCurve(AudioSourceCurveType.ReverbZoneMix, aC2);
                aS[i].SetCustomCurve(AudioSourceCurveType.Spread, aC2);
            }
            for (int i = 0; i < simulated.Count; i++)
            {
                simulated[i].SetCustomCurve(AudioSourceCurveType.ReverbZoneMix, aC2);
                simulated[i].SetCustomCurve(AudioSourceCurveType.Spread, aC2);
            }
            if (this.GetComponent<AudioSource>() != null)
            {
                this.GetComponent<AudioSource>().SetCustomCurve(AudioSourceCurveType.ReverbZoneMix, aC2);
                this.GetComponent<AudioSource>().SetCustomCurve(AudioSourceCurveType.Spread, aC2);
            }
        }

        private AudioSource[] GetAllAudioListeners(Transform t)
        {
            List<AudioSource> l = new List<AudioSource>();
            for (int i = 0; i < t.childCount; i++)
            {
                if (t.GetChild(i).GetComponent<AudioSource>() != null)
                    l.Add(t.GetChild(i).GetComponent<AudioSource>());
                else l.AddRange(GetAllAudioListeners(t.GetChild(i)));
            }
            return l.ToArray();
        }
    }
}
