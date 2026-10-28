using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using CycloneGames.Scheduling.Core;
using NUnit.Framework;

namespace CycloneGames.Scheduling.Tests.Editor
{
    /// <summary>
    /// Pins the public API surface of the module. Any accidental rename, removal, or signature
    /// change fails here instead of failing in a consumer project months later.
    /// </summary>
    public sealed class PublicApiGuardTests
    {
        [Test]
        public void PublicSurface_MatchesPinnedContract()
        {
            var actual = new List<string>();
            Collect(actual, typeof(WorkScheduler));
            Collect(actual, typeof(WorkLane));
            Collect(actual, typeof(WorkItemHandle));
            Collect(actual, typeof(SchedulerSnapshot));
            Collect(actual, typeof(IWorkItem));
            Collect(actual, typeof(WorkStepResult));
            Collect(actual, typeof(WorkTerminationReason));

            actual.Sort(StringComparer.Ordinal);

            var builder = new StringBuilder();
            for (int index = 0; index < actual.Count; index++)
            {
                builder.Append(actual[index]).Append('\n');
            }

            string expected = PinnedSurface.Replace("\r\n", "\n").Trim();
            string observed = builder.ToString().Trim();

            if (!string.Equals(expected, observed, StringComparison.Ordinal))
            {
                Assert.Fail("Public API surface changed.\n--- expected ---\n" + expected
                    + "\n--- actual ---\n" + observed);
            }
        }

        private static void Collect(List<string> lines, Type type)
        {
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static
                | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            if (!type.IsEnum)
            {
                foreach (FieldInfo field in type.GetFields(Flags))
                {
                    lines.Add(type.Name + " field " + field.Name + " : " + Name(field.FieldType));
                }
            }

            foreach (PropertyInfo property in type.GetProperties(Flags))
            {
                MethodInfo getter = property.GetGetMethod();
                MethodInfo setter = property.GetSetMethod();
                lines.Add(type.Name + " prop " + property.Name + " : " + Name(property.PropertyType)
                    + (getter != null ? " get" : "")
                    + (setter != null ? " set" : ""));
            }

            foreach (MethodInfo method in type.GetMethods(Flags))
            {
                if (method.IsSpecialName)
                {
                    continue;
                }

                var signature = new StringBuilder();
                signature.Append(type.Name).Append(" method ").Append(method.Name).Append('(');
                ParameterInfo[] parameters = method.GetParameters();
                for (int index = 0; index < parameters.Length; index++)
                {
                    if (index > 0)
                    {
                        signature.Append(", ");
                    }

                    signature.Append(Name(parameters[index].ParameterType));
                }

                signature.Append(") : ").Append(Name(method.ReturnType));
                lines.Add(signature.ToString());
            }

            if (type.IsEnum)
            {
                foreach (string name in Enum.GetNames(type))
                {
                    lines.Add(type.Name + " value " + name + " = "
                        + Convert.ToInt64(Enum.Parse(type, name)).ToString());
                }
            }

            foreach (ConstructorInfo ctor in type.GetConstructors(Flags))
            {
                var signature = new StringBuilder();
                signature.Append(type.Name).Append(" ctor(");
                ParameterInfo[] parameters = ctor.GetParameters();
                for (int index = 0; index < parameters.Length; index++)
                {
                    if (index > 0)
                    {
                        signature.Append(", ");
                    }

                    signature.Append(Name(parameters[index].ParameterType));
                }

                signature.Append(')');
                lines.Add(signature.ToString());
            }
        }

        private static string Name(Type type)
        {
            if (type.IsEnum)
            {
                return type.Name;
            }

            return type.Name;
        }

        private const string PinnedSurface = @"
IWorkItem method OnTerminated(WorkTerminationReason) : Void
IWorkItem method Step() : WorkStepResult
SchedulerSnapshot prop AcceptedCount : Int64 get
SchedulerSnapshot prop CancelledCount : Int64 get
SchedulerSnapshot prop Capacity : Int32 get
SchedulerSnapshot prop CompletedCount : Int64 get
SchedulerSnapshot prop FaultedCount : Int64 get
SchedulerSnapshot prop IsShutdown : Boolean get
SchedulerSnapshot prop IsShutdownCompleted : Boolean get
SchedulerSnapshot prop LastTickMilliseconds : Double get
SchedulerSnapshot prop MaxTickMilliseconds : Double get
SchedulerSnapshot prop OutstandingCount : Int32 get
SchedulerSnapshot prop OverBudgetTickCount : Int64 get
SchedulerSnapshot prop RejectedCount : Int64 get
SchedulerSnapshot prop ShutdownCount : Int64 get
SchedulerSnapshot prop SlowestStep : WorkItemHandle get
SchedulerSnapshot prop SlowestStepMilliseconds : Double get
SchedulerSnapshot prop StepCount : Int64 get
SchedulerSnapshot prop TickCount : Int64 get
WorkItemHandle method BelongsTo(UInt32) : Boolean
WorkItemHandle method Equals(Object) : Boolean
WorkItemHandle method Equals(WorkItemHandle) : Boolean
WorkItemHandle method GetHashCode() : Int32
WorkItemHandle prop Generation : UInt32 get
WorkItemHandle prop IsDefault : Boolean get
WorkItemHandle prop SchedulerId : UInt32 get
WorkItemHandle prop Slot : Int32 get
WorkLane value Background = 3
WorkLane value High = 1
WorkLane value Immediate = 0
WorkLane value Normal = 2
WorkScheduler ctor(Int32)
WorkScheduler ctor(Int32, Int32)
WorkScheduler field DefaultStarvationLimitTicks : Int32
WorkScheduler field LaneCount : Int32
WorkScheduler method Cancel(WorkItemHandle) : Boolean
WorkScheduler method Dispose() : Void
WorkScheduler method GetLaneOutstanding(WorkLane) : Int32
WorkScheduler method GetSnapshot() : SchedulerSnapshot
WorkScheduler method Shutdown() : Void
WorkScheduler method ShutdownStep(Int32) : Int32
WorkScheduler method Tick(Int32, Double) : Int32
WorkScheduler method TrySchedule(IWorkItem, WorkItemHandle&) : Boolean
WorkScheduler method TrySchedule(IWorkItem, WorkLane, WorkItemHandle&) : Boolean
WorkScheduler prop Capacity : Int32 get
WorkScheduler prop IsShutdown : Boolean get
WorkScheduler prop IsShutdownCompleted : Boolean get
WorkScheduler prop OutstandingCount : Int32 get
WorkScheduler prop SchedulerId : UInt32 get
WorkScheduler prop StarvationLimitTicks : Int32 get
WorkScheduler prop TrackStepTiming : Boolean get set
WorkStepResult value Completed = 1
WorkStepResult value Running = 0
WorkTerminationReason value Cancelled = 1
WorkTerminationReason value Completed = 0
WorkTerminationReason value Faulted = 2
WorkTerminationReason value Shutdown = 3";
    }
}
