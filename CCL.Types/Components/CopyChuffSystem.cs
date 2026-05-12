using CCL.Types.Proxies.Ports;
using System.Collections.Generic;
using UnityEngine;

namespace CCL.Types.Components
{
    [AddComponentMenu("CCL/Components/Copiers/Copy Chuff System")]
    public class CopyChuffSystem : MonoBehaviour, IInstancedObject<GameObject>, IHasPortIdFields
    {
        public enum Locomotive
        {
            S060,
            S282
        }

        public Locomotive LocomotiveType = Locomotive.S282;

        [Header("Ports")]
        [PortId(DVPortValueType.STATE, false)]
        public string chuffEventPortId = string.Empty;
        [PortId(DVPortValueType.PRESSURE, false)]
        public string exhaustPressurePortId = string.Empty;
        [PortId(DVPortValueType.STATE, false)]
        public string chuffFrequencyPortId = string.Empty;
        [PortId(DVPortValueType.STATE, false)]
        public string cylinderWaterNormalizedPortId = string.Empty;
        [PortId(DVPortValueType.CONTROL, false)]
        public string cylinderCockControlPortId = string.Empty;
        [PortId(DVPortValueType.STATE, false)]
        public string ashesInPipesPortId = string.Empty;

        [Header("Individual Chuffs")]
        public bool customVolumeCurve = false;
        [EnableIf(nameof(customVolumeCurve))]
        public AnimationCurve pressureToVolumeCurve = null!;
        public float mediumPressureThreshold = 6;
        public float highPressureThreshold = 9;

        public GameObject? InstancedObject { get; set; }
        public bool CanReplace => InstancedObject != null;

        public IEnumerable<PortIdField> ExposedPortIdFields => new[]
{
            new PortIdField(this, nameof(chuffEventPortId), chuffEventPortId, DVPortValueType.STATE),
            new PortIdField(this, nameof(exhaustPressurePortId), exhaustPressurePortId, DVPortValueType.PRESSURE),
            new PortIdField(this, nameof(chuffFrequencyPortId), chuffFrequencyPortId, DVPortValueType.STATE),
            new PortIdField(this, nameof(cylinderWaterNormalizedPortId), cylinderWaterNormalizedPortId, DVPortValueType.STATE),
            new PortIdField(this, nameof(cylinderCockControlPortId), cylinderCockControlPortId, DVPortValueType.CONTROL),
            new PortIdField(this, nameof(ashesInPipesPortId), ashesInPipesPortId, DVPortValueType.STATE),
        };

        private static AnimationCurve DefaultCurve => new AnimationCurve()
        {
            preWrapMode = WrapMode.ClampForever,
            postWrapMode = WrapMode.ClampForever,
            keys = new[]
                {
                    new Keyframe
                    {
                        time = 0.0f,
                        value = 0.0f,
                        inTangent = -0.008165377f,
                        outTangent = -0.008165377f,
                        inWeight = 0.0f,
                        outWeight = 1.0f,
                    },
                    new Keyframe
                    {
                        time = 1.0f,
                        value = 0.0f,
                        inTangent = 0.0f,
                        outTangent = 0.0f,
                        inWeight = 1 / 3f,
                        outWeight = 0.37559202f,
                    },
                    new Keyframe
                    {
                        time = 2.0f,
                        value = 0.6986543f,
                        inTangent = 0.09688346f,
                        outTangent = 0.09688346f,
                        inWeight = 0.2691377f,
                        outWeight = 0.040892176f,
                    },
                    new Keyframe
                    {
                        time = 11.0f,
                        value = 1.0f,
                        inTangent = -0.0f,
                        outTangent = -0.0f,
                        inWeight = 0.091343455f,
                        outWeight = 0.0f,
                    }
                }
        };

        private void OnReset()
        {
            pressureToVolumeCurve = DefaultCurve;
        }
    }
}
