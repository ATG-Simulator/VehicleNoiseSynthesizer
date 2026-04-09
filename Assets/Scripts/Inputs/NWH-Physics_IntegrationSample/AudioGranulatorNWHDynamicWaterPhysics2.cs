using NWH.DWP2;
using NWH.DWP2.ShipController;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static System.TimeZoneInfo;

namespace AroundTheGroundSimulator
{
    // NWH Vehicle integration sample
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

            aG.Activate(e.maxRPM, e.minRPM);
        }
        private void FixedUpdate()
        {
            if (e.isOn) //NWH Dynamic Water Physics does not use Events for its engines so every fixed frame this should be checked... .
                aG.TurnOn();
            else
                aG.TurnOff();

            aG.load = Mathf.Lerp(aG.load,Mathf.Clamp01(Mathf.Abs(e.Thrust) / e.maxThrust), Time.deltaTime * loadSmoothenIntensity);
            aG.rpm = Mathf.Lerp(aG.rpm, e.RPM, Time.deltaTime * rpmSmoothenIntensity);
        }
    }
}