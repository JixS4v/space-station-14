namespace Content.Shared.Atmos.Components
{
    [RegisterComponent]

    public sealed class AtmosLayerComponent : Component
    {
        public int Layer = 3;

        [DataField("allLayers")]
        public bool AllLayers = false;

        [DataField("requiresOffset")]
        public bool RequiresOffset = false;
    }
}
