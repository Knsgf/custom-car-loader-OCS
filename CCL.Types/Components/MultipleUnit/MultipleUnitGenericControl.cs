using CCL.Types.Proxies.Ports;
using System.Collections.Generic;
using UnityEngine;

namespace CCL.Types.Components.MultipleUnit
{
    [AddComponentMenu("CCL/Components/Multiple Unit/Multiple Unit Generic Control")]
    public class MultipleUnitGenericControl : MultipleUnitExtraControl<MultipleUnitGenericControl>, IHasPortIdFields
    {
        [MultipleUnitGenericControlId]
        public string ConnectionId = string.Empty;
        [PortId]
        public string PortId = string.Empty;
        public bool ResetOnConnectionChange = true;
        [EnableIf(nameof(ResetOnConnectionChange))]
        public float DefaultValue = 0;
        public bool EnsureNotches = false;
        [EnableIf(nameof(EnsureNotches)), Min(2)]
        public int Notches = 2;

        public IEnumerable<PortIdField> ExposedPortIdFields => new[]
        {
            new PortIdField(this, nameof(PortId), PortId),
        };
    }

    public class MultipleUnitGenericControlIdAttribute : StringAndSelectorFieldAttribute
    {
        private static readonly string[] s_ids = new[]
        {
            "GEARBOX_A",
            "GEARBOX_B",
        };

        public MultipleUnitGenericControlIdAttribute() : base(s_ids, true) { }
    }
}
