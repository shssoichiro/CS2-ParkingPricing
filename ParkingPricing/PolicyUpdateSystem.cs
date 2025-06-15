using System;
using Game;
using Unity.Collections;
using Unity.Entities;

namespace ParkingPricing {
    // System to process policy update commands immediately after ECB playback
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ParkingPricingEntityCommandBufferSystem))]
    public partial class PolicyUpdateSystem : GameSystemBase {
        private PolicyManager _policyManager;
        private EntityQuery _policyUpdateQuery;

        protected override void OnCreate() {
            base.OnCreate();
            _policyManager = new PolicyManager(EntityManager);
            _policyUpdateQuery = GetEntityQuery(ComponentType.ReadOnly<PolicyUpdateCommand>());
        }

        protected override void OnUpdate() {
            if (_policyUpdateQuery.IsEmpty) {
                return;
            }

            LogUtil.Debug(
                $"PolicyUpdateSystem.OnUpdate: Query has {_policyUpdateQuery.CalculateEntityCount()} entities"
            );
            int appliedUpdates = 0;
            NativeArray<PolicyUpdateCommand> policyUpdateCommands =
                _policyUpdateQuery.ToComponentDataArray<PolicyUpdateCommand>(Allocator.Temp);
            NativeArray<Entity> entities = _policyUpdateQuery.ToEntityArray(Allocator.Temp);

            try {
                for (int i = 0; i < entities.Length; i++) {
                    Entity entity = entities[i];
                    PolicyUpdateCommand command = policyUpdateCommands[i];

                    try {
                        // Remove the command component
                        EntityManager.RemoveComponent<PolicyUpdateCommand>(entity);

                        // Apply the policy update
                        _policyManager.UpdateOrAddPolicy(entity, command.PolicyPrefab, command.NewPrice);

                        LogUtil.Info(
                            command.IsDistrict
                                ? $"Updated street parking policy for district {entity.Index}: ${command.NewPrice} (Utilization: {command.Utilization:P2})"
                                : $"Updated parking policy for building {entity.Index}: ${command.NewPrice} (Utilization: {command.Utilization:P2})"
                        );

                        appliedUpdates++;
                    } catch (Exception ex) {
                        LogUtil.Error($"Failed to apply pricing update to entity {entity.Index}: {ex.Message}");
                    }
                }

                if (appliedUpdates > 0) {
                    LogUtil.Info($"Applied {appliedUpdates} pricing updates immediately via ECB");
                }
            } finally {
                policyUpdateCommands.Dispose();
                entities.Dispose();
            }
        }
    }
}
