using Game.Policies;
using Unity.Entities;

namespace ParkingPricing
{
    // Extracted policy management logic following SRP
    public class PolicyManager
    {
        private readonly EntityManager entityManager;

        public PolicyManager(EntityManager entityManager)
        {
            this.entityManager = entityManager;
        }

        public bool TryGetPolicy(Entity entity, Entity policyType, out Policy policy)
        {
            if (entityManager.Exists(entity) && entityManager.HasBuffer<Policy>(entity))
            {
                var buffer = entityManager.GetBuffer<Policy>(entity);
                for (var index = 0; index < buffer.Length; index++)
                {
                    if (buffer[index].m_Policy == policyType)
                    {
                        policy = buffer[index];
                        return true;
                    }
                }
            }
            policy = default(Policy);
            return false;
        }

        public void UpdateOrAddPolicy(Entity entity, Entity policyType, int newPrice)
        {
            if (!entityManager.Exists(entity) || !entityManager.HasBuffer<Policy>(entity))
            {
                LogUtil.Warn($"Entity {entity.Index} does not exist or lacks Policy buffer");
                return;
            }

            var buffer = entityManager.GetBuffer<Policy>(entity);
            for (var index = 0; index < buffer.Length; index++)
            {
                if (buffer[index].m_Policy == policyType)
                {
                    // Update the existing policy
                    var policy = buffer[index];
                    policy.m_Adjustment = newPrice;
                    policy.m_Flags = newPrice > 0 ? PolicyFlags.Active : 0;
                    buffer[index] = policy;
                    return;
                }
            }

            // No existing policy, so add it as a new one
            var newPolicy = new Policy
            {
                m_Adjustment = newPrice,
                m_Flags = newPrice > 0 ? PolicyFlags.Active : 0,
                m_Policy = policyType
            };
            buffer.Add(newPolicy);
        }
    }
}