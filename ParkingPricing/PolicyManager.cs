using Colossal.Entities;
using Game.Policies;
using Unity.Entities;

namespace ParkingPricing {
    // Extracted policy management logic following SRP
    public class PolicyManager {
        private EntityManager _entityManager;

        public PolicyManager(EntityManager entityManager) {
            _entityManager = entityManager;
        }

        public bool HasPolicy(Entity entity, Entity policyType) {
            if (!_entityManager.Exists(entity)
                || !_entityManager.TryGetBuffer(entity, true, out DynamicBuffer<Policy> buffer)) {
                return false;
            }

            for (int index = 0; index < buffer.Length; index++) {
                if (buffer[index].m_Policy != policyType) {
                    continue;
                }

                return true;
            }

            return false;
        }

        public void UpdateOrAddPolicy(Entity entity, Entity policyType, int newPrice) {
            if (!_entityManager.Exists(entity) || !_entityManager.TryGetBuffer(
                    entity, false, out DynamicBuffer<Policy> buffer
                )) {
                LogUtil.Warn($"Entity {entity.Index} does not exist or lacks Policy buffer");
                return;
            }

            for (int index = 0; index < buffer.Length; index++) {
                if (buffer[index].m_Policy != policyType) {
                    continue;
                }

                // Update the existing policy
                Policy policy = buffer[index];
                policy.m_Adjustment = newPrice;
                policy.m_Flags = newPrice > 0 ? PolicyFlags.Active : 0;
                buffer[index] = policy;
                return;
            }

            // No existing policy, so add it as a new one
            var newPolicy = new Policy {
                m_Adjustment = newPrice, m_Flags = newPrice > 0 ? PolicyFlags.Active : 0, m_Policy = policyType
            };
            buffer.Add(newPolicy);
        }
    }
}
