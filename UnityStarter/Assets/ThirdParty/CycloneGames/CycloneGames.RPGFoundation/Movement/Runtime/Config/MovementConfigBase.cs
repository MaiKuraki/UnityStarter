using UnityEngine;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime
{
    public abstract class MovementConfigBase : ScriptableObject
    {
        [Min(0.0f)]
        [Tooltip("Unit: m/s. Valid range: 0 .. 50.")]
        [SerializeField] private float walkSpeed = 1.5f;
        [Min(0.0f)]
        [Tooltip("Unit: m/s. Valid range: 0 .. 50.")]
        [SerializeField] private float runSpeed = 3f;
        [Min(0.0f)]
        [Tooltip("Unit: m/s. Valid range: 0 .. 50.")]
        [SerializeField] private float sprintSpeed = 6f;
        [Min(0.0f)]
        [Tooltip("Unit: m/s. Valid range: 0 .. 50.")]
        [SerializeField] private float crouchSpeed = 1f;

        [Min(0.0f)]
        [Tooltip("Unit: m/s. Valid range: 0 .. 100.")]
        [SerializeField] private float jumpForce = 6f;
        [Min(0.0f)]
        [Tooltip("Unit: count. Valid range: 0 .. 10.")]
        [SerializeField] private int maxJumpCount = 1;

        public float WalkSpeed => walkSpeed;
        public float RunSpeed => runSpeed;
        public float SprintSpeed => sprintSpeed;
        public float CrouchSpeed => crouchSpeed;
        public float JumpForce => jumpForce;
        public int MaxJumpCount => maxJumpCount;

        protected void SanitizeBaseFields(MovementConfigRules rules)
        {
            MovementConfigIssueCode ignored;
            walkSpeed = rules.Sanitize(MovementConfigField.WalkSpeed, walkSpeed, out ignored);
            runSpeed = rules.Sanitize(MovementConfigField.RunSpeed, runSpeed, out ignored);
            sprintSpeed = rules.Sanitize(MovementConfigField.SprintSpeed, sprintSpeed, out ignored);
            crouchSpeed = rules.Sanitize(MovementConfigField.CrouchSpeed, crouchSpeed, out ignored);
            jumpForce = rules.Sanitize(MovementConfigField.JumpForce, jumpForce, out ignored);
            maxJumpCount = rules.SanitizeJumpCount(maxJumpCount, out ignored);
        }

        protected virtual void WriteNumericValues(float[] destination)
        {
            destination[(int)MovementConfigField.WalkSpeed] = walkSpeed;
            destination[(int)MovementConfigField.RunSpeed] = runSpeed;
            destination[(int)MovementConfigField.SprintSpeed] = sprintSpeed;
            destination[(int)MovementConfigField.CrouchSpeed] = crouchSpeed;
            destination[(int)MovementConfigField.JumpForce] = jumpForce;
            destination[(int)MovementConfigField.MaxJumpCount] = maxJumpCount;
        }

        public string ValidateConfiguration()
        {
            var rules = new MovementConfigRules();
            var values = new float[MovementConfigRules.FieldCount];
            rules.WriteDefaults(values);
            WriteNumericValues(values);
            var issues = new MovementConfigIssue[MovementConfigRules.FieldCount + 4];
            int count = rules.Validate(values, issues);
            if (count == 0)
                return "OK: all " + MovementConfigRules.FieldCount + " numeric fields are within their documented ranges.";
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++) builder.Append(issues[i].Describe(rules));
            return builder.ToString();
        }

    }
}