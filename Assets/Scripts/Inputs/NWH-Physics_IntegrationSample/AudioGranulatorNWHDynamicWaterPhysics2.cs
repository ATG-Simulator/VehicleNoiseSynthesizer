using NWH.DWP2;
using NWH.DWP2.ShipController;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace AroundTheGroundSimulator
{
    /// <summary>Vehicle Noise Synthesizer v1.9 — NWH Dynamic Water Physics 2 integration sample.</summary>
    [RequireComponent(typeof(VehicleNoiseSynthesizer))]
    public class AudioGranulatorNWHDynamicWaterPhysics2 : MonoBehaviour
    {
        VehicleNoiseSynthesizer aG;

        public int engineIndex = 0;
        public float rpmSmoothenIntensity = 10f;
        public float loadSmoothenIntensity = 0.1f;

        AdvancedShipController asc;
        Engine e;
        float eps;
        void OnEnable()
        {
            aG = GetComponent<VehicleNoiseSynthesizer>();
            asc = this.GetComponentInParent<AdvancedShipController>();

            e = asc.engines[engineIndex];
            eps = Mathf.Epsilon;

            aG.Activate();
        }
        private void FixedUpdate()
        {
            if (e.isOn)
                aG.TurnOn();
            else
                aG.TurnOff();

            aG.load = Mathf.Lerp(aG.load, Mathf.Clamp01(Mathf.Abs(e.Thrust) / e.maxThrust), Time.deltaTime * loadSmoothenIntensity);
            aG.rpm = Mathf.Lerp(aG.rpm, e.RPM, Time.deltaTime * rpmSmoothenIntensity);
        }
    }
}