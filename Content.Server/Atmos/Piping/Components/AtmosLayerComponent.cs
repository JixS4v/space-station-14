namespace Content.Server.Atmos.Piping.Components
{
    [RegisterComponent]

    public sealed class AtmosLayerComponent : Component
    {
        public int Layer = 3;

        [DataField("allLayers")]
        public bool AllLayers = false;
    }
}
