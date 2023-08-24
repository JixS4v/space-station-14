namespace Content.Shared.Atmos.Components
{
    [RegisterComponent, NetworkedComponent]
    [AutoGenerateComponentState]
    public sealed class AtmosLayerComponent : Component
    {
        [AutoNetworkedField]
        public int Layer = 3;

        [DataField("allLayers")]
        public bool AllLayers = false;

        [DataField("requiresOffset")]
        public bool RequiresOffset = false;
    }
}
