using NWH.VehiclePhysics2;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AroundTheGroundSimulator
{
    // NWH Vehicle integration sample
    [RequireComponent(typeof(VehicleNoiseSynthesizer))]
    public class AudioGranulatorNWHVehiclePhysics2 : MonoBehaviour
    {
        VehicleNoiseSynthesizer aG;

        VehicleController vp;
        void OnEnable()
        {
            aG = GetComponent<VehicleNoiseSynthesizer>();
            vp = this.GetComponentInParent<VehicleController>();

            vp.powertrain.engine.onStart.AddListener(aG.TurnOn); //NWH Integration to know if the vehicle is on or off
            vp.powertrain.engine.onStop.AddListener(aG.TurnOff); //NWH Integration to know if the vehicle is on or off

            aG.Activate(vp.powertrain.engine.revLimiterRPM, vp.powertrain.engine.idleRPM);
        }

        void OnDisable()
        {
            vp.powertrain.engine.onStart.RemoveListener(aG.TurnOn); //NWH Integration to know if the vehicle is on or off
            vp.powertrain.engine.onStop.RemoveListener(aG.TurnOff); //NWH Integration to know if the vehicle is on or off
        }

        private void FixedUpdate()
        {
            aG.load = vp.powertrain.engine.Load;
            aG.rpm = vp.powertrain.engine.OutputRPM;
        }
    }
}