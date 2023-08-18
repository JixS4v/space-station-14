using Content.Server.Atmos;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.NodeGroups;
using Content.Shared.Atmos;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server.NodeContainer.Nodes
{
    /// <summary>
    ///     Connects with other <see cref="PipeNode"/>s whose <see cref="PipeDirection"/>
    ///     correctly correspond.
    /// </summary>
    [DataDefinition]
    [Virtual]
    public class PipeNode : Node, IGasMixtureHolder, IRotatableNode
    {
        /// <summary>
        ///     The directions in which this pipe can connect to other pipes around it.
        /// </summary>
        [DataField("pipeDirection")]
        private PipeDirection _originalPipeDirection;

        private int Layer = 3;

        private bool AllLayers = false;

        /// <summary>
        ///     The *current* pipe directions (accounting for rotation)
        ///     Used to check if this pipe can connect to another pipe in a given direction.
        /// </summary>
        public PipeDirection CurrentPipeDirection { get; private set; }

        private HashSet<PipeNode>? _alwaysReachable;

        public void AddAlwaysReachable(PipeNode pipeNode)
        {
            if (pipeNode.NodeGroupID != NodeGroupID) return;
            _alwaysReachable ??= new();
            _alwaysReachable.Add(pipeNode);

            if (NodeGroup != null)
                EntitySystem.Get<NodeGroupSystem>().QueueRemakeGroup((BaseNodeGroup) NodeGroup);
        }

        public void RemoveAlwaysReachable(PipeNode pipeNode)
        {
            if (_alwaysReachable == null) return;

            _alwaysReachable.Remove(pipeNode);

            if (NodeGroup != null)
                EntitySystem.Get<NodeGroupSystem>().QueueRemakeGroup((BaseNodeGroup) NodeGroup);
        }

        /// <summary>
        ///     Whether this node can connect to others or not.
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite)]
        public bool ConnectionsEnabled
        {
            get => _connectionsEnabled;
            set
            {
                _connectionsEnabled = value;

                if (NodeGroup != null)
                    EntitySystem.Get<NodeGroupSystem>().QueueRemakeGroup((BaseNodeGroup) NodeGroup);
            }
        }

        [DataField("connectionsEnabled")]
        private bool _connectionsEnabled = true;

        public override bool Connectable(IEntityManager entMan, TransformComponent? xform = null)
        {
            return _connectionsEnabled && base.Connectable(entMan, xform);
        }

        [DataField("rotationsEnabled")]
        public bool RotationsEnabled { get; set; } = true;

        /// <summary>
        ///     The <see cref="IPipeNet"/> this pipe is a part of.
        /// </summary>
        [ViewVariables]
        private IPipeNet? PipeNet => (IPipeNet?) NodeGroup;

        /// <summary>
        ///     The gases in this pipe.
        /// </summary>
        [ViewVariables]
        public GasMixture Air
        {
            get => PipeNet?.Air ?? GasMixture.SpaceGas;
            set
            {
                DebugTools.Assert(PipeNet != null);
                PipeNet!.Air = value;
            }
        }

        [DataField("volume")]
        public float Volume { get; set; } = DefaultVolume;

        private const float DefaultVolume = 200f;

        public override void Initialize(EntityUid owner, IEntityManager entMan)
        {
            base.Initialize(owner, entMan);

            if (RotationsEnabled)
            {
                var xform = entMan.GetComponent<TransformComponent>(owner);
                CurrentPipeDirection = _originalPipeDirection.RotatePipeDirection(xform.LocalRotation);
            }
            /// <summary>
            ///     we fetch the Atmos layer properties of the entity from the component
            /// </summary>
            if (!entMan.TryGetComponent<AtmosLayerComponent>(owner, out var layerComponent))
                return;

            Layer = layerComponent.Layer;
            AllLayers = layerComponent.AllLayers;
        }

        bool IRotatableNode.RotateNode(in MoveEvent ev)
        {
            if (_originalPipeDirection == PipeDirection.Fourway)
                return false;

            // update valid pipe direction
            if (!RotationsEnabled)
            {
                if (CurrentPipeDirection == _originalPipeDirection)
                    return false;

                CurrentPipeDirection = _originalPipeDirection;
                return true;
            }

            var oldDirection = CurrentPipeDirection;
            CurrentPipeDirection = _originalPipeDirection.RotatePipeDirection(ev.NewRotation);
            return oldDirection != CurrentPipeDirection;
        }

        public override void OnAnchorStateChanged(IEntityManager entityManager, bool anchored)
        {
            if (!anchored)
                return;

            // update valid pipe directions

            if (!RotationsEnabled)
            {
                CurrentPipeDirection = _originalPipeDirection;
                return;
            }

            var xform = entityManager.GetComponent<TransformComponent>(Owner);
            CurrentPipeDirection = _originalPipeDirection.RotatePipeDirection(xform.LocalRotation);
        }

        public override IEnumerable<Node> GetReachableNodes(TransformComponent xform,
            EntityQuery<NodeContainerComponent> nodeQuery,
            EntityQuery<TransformComponent> xformQuery,
            MapGridComponent? grid,
            IEntityManager entMan)
        {
            if (_alwaysReachable != null)
            {
                var remQ = new RemQueue<PipeNode>();
                foreach (var pipe in _alwaysReachable)
                {
                    if (pipe.Deleting)
                    {
                        remQ.Add(pipe);
                    }
                    yield return pipe;
                }

                foreach (var pipe in remQ)
                {
                    _alwaysReachable.Remove(pipe);
                }
            }

            if (!xform.Anchored || grid == null)
                yield break;

            var pos = grid.TileIndicesFor(xform.Coordinates);

            for (var i = 0; i < PipeDirectionHelpers.PipeDirections; i++)
            {
                var pipeDir = (PipeDirection) (1 << i);

                if (!CurrentPipeDirection.HasDirection(pipeDir))
                    continue;

                foreach (var pipe in LinkableNodesInDirection(pos, pipeDir, grid, nodeQuery))
                {
                    yield return pipe;
                }
            }
        }

        /// <summary>
        ///     Gets the pipes that can connect to us from entities on the tile or adjacent in a direction.
        /// </summary>
        private IEnumerable<PipeNode> LinkableNodesInDirection(Vector2i pos, PipeDirection pipeDir, MapGridComponent grid,
            EntityQuery<NodeContainerComponent> nodeQuery)
        {
            foreach (var pipe in PipesInDirection(pos, pipeDir, grid, nodeQuery))
            {
                if (pipe.NodeGroupID == NodeGroupID
                    && pipe.CurrentPipeDirection.HasDirection(pipeDir.GetOpposite())
                    && (pipe.Layer == Layer || AllLayers == true || pipe.AllLayers == true))
                {
                    yield return pipe;
                }
            }
        }

        /// <summary>
        ///     Gets the pipes from entities on the tile adjacent in a direction.
        /// </summary>
        protected IEnumerable<PipeNode> PipesInDirection(Vector2i pos, PipeDirection pipeDir, MapGridComponent grid,
            EntityQuery<NodeContainerComponent> nodeQuery)
        {
            var offsetPos = pos.Offset(pipeDir.ToDirection());

            foreach (var entity in grid.GetAnchoredEntities(offsetPos))
            {
                if (!nodeQuery.TryGetComponent(entity, out var container))
                    continue;

                foreach (var node in container.Nodes.Values)
                {
                    if (node is PipeNode pipe)
                        yield return pipe;
                }
            }
        }
    }
}
