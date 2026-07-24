using CCL.Types.Components.MultipleUnit;
using DV.MultipleUnit;
using LocoSim.Attributes;
using LocoSim.Implementations;
using UnityEngine;

namespace CCL.Importer.Components.MultipleUnit
{
    internal class MultipleUnitGenericControlInternal :
        MultipleUnitExtraControlInternal<MultipleUnitGenericControlInternal>
    {
        [MultipleUnitGenericControlId]
        public string ConnectionId = string.Empty;
        [PortId]
        public string PortId = string.Empty;
        public bool ResetOnConnectionChange = true;
        public float DefaultValue = 0;
        public bool EnsureNotches = false;
        public int Notches = 2;

        private Port _port = null!;

        public override void Init(TrainCar car, SimulationFlow simFlow)
        {
            base.Init(car, simFlow);

            if (!simFlow.TryGetPort(PortId, out _port, false))
            {
                Debug.LogError($"(MultipleUnitGenericControl) Could not find port!", this);
                Destroy(this);
                return;
            }

            _port.ValueUpdatedInternally += (x) => ValueChanged();
        }

        public override void SetValue(MultipleUnitGenericControlInternal source)
        {
            _port.Value = EnsureNotches ? Mathf.RoundToInt(source._port.Value * (Notches - 1)) / (Notches - 1f) : source._port.Value;
        }

        protected override void ConnectionChanged(bool connected, bool playAudio)
        {
            if (ResetOnConnectionChange)
            {
                _port.Value = DefaultValue;
            }
        }

        protected override void TrySetValue(MultipleUnitModule module)
        {
            foreach (var comp in module.train.GetComponents<MultipleUnitGenericControlInternal>())
            {
                if (comp.ConnectionId != ConnectionId) continue;

                comp.SetValue(this);
            }
        }
    }
}
