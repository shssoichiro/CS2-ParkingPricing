using Game.Policies;
using Unity.Entities;

namespace ParkingPricing {
    // Extracted policy management logic following SRP
    public class PolicyManager {
        private EntityManager _entityManager;

        public PolicyManager(EntityManager entityManager) {
            _entityManager = entityManager;
        }

        public bool TryGetPolicy(Entity entity, Entity policyType, out Policy policy) {
            if (_entityManager.Exists(entity) && _entityManager.HasBuffer<Policy>(entity)) {
                DynamicBuffer<Policy> buffer = _entityManager.GetBuffer<Policy>(entity);
                for (int index = 0; index < buffer.Length; index++) {
                    if (buffer[index].m_Policy != policyType) {
                        continue;
                    }

                    policy = buffer[index];
                    return true;
                }
            }

            policy = default;
            return false;
        }

        public void UpdateOrAddPolicy(Entity entity, Entity policyType, int newPrice) {
            if (!_entityManager.Exists(entity) || !_entityManager.HasBuffer<Policy>(entity)) {
                LogUtil.Warn($"Entity {entity.Index} does not exist or lacks Policy buffer");
                return;
            }

            DynamicBuffer<Policy> buffer = _entityManager.GetBuffer<Policy>(entity);
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
