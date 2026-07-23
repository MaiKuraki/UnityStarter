using System;
using System.Collections.Generic;
using CycloneGames.Audio.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Audio.Tests.Editor
{
    public sealed class AudioBankRegistryOwnershipTests
    {
        private const string CollisionParameterName = "AudioBankRegistryTests.SharedParameter";
        private const string CollisionStateGroupName = "AudioBankRegistryTests.SharedStateGroup";

        private readonly List<AudioBank> banks = new List<AudioBank>(4);
        private readonly List<ScriptableObject> createdObjects = new List<ScriptableObject>(16);

        private GameObject managerObject;
        private Action<AudioBank> bankUnloadedHandler;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNull(AudioManager.Instance, "A previous test left an AudioManager instance alive.");

            managerObject = new GameObject("AudioBankRegistryOwnershipTests.AudioManager");
            managerObject.SetActive(false);
            managerObject.AddComponent<AudioListener>();

            AudioManager manager = managerObject.AddComponent<AudioManager>();
            var serializedManager = new SerializedObject(manager);
            SerializedProperty customPoolSize = serializedManager.FindProperty("customPoolSize");
            Assert.NotNull(customPoolSize, "AudioManager.customPoolSize could not be found.");
            customPoolSize.intValue = 1;
            serializedManager.ApplyModifiedPropertiesWithoutUndo();

            managerObject.SetActive(true);
            AudioManager.SetInstance(manager);
        }

        [TearDown]
        public void TearDown()
        {
            if (bankUnloadedHandler != null)
            {
                AudioManager.OnBankUnloaded -= bankUnloadedHandler;
                bankUnloadedHandler = null;
            }

            if (AudioManager.Instance != null)
            {
                for (int i = 0; i < banks.Count; i++)
                {
                    AudioBank bank = banks[i];
                    if (bank != null)
                    {
                        AudioManager.UnloadBank(bank);
                    }
                }
            }

            if (managerObject != null)
            {
                AudioManager.ReleaseInstance(managerObject.GetComponent<AudioManager>());
                UnityEngine.Object.DestroyImmediate(managerObject);
                managerObject = null;
            }

            for (int i = createdObjects.Count - 1; i >= 0; i--)
            {
                ScriptableObject createdObject = createdObjects[i];
                if (createdObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdObject);
                }
            }

            banks.Clear();
            createdObjects.Clear();
        }

        [Test]
        public void ReleaseDestroyedManager_ClearsSingletonReference()
        {
            AudioManager manager = managerObject.GetComponent<AudioManager>();
            UnityEngine.Object.DestroyImmediate(managerObject);
            managerObject = null;

            Assert.IsTrue(manager == null, "DestroyImmediate must produce a Unity fake-null manager.");

            AudioManager.ReleaseInstance(manager);

            Assert.IsTrue(
                ReferenceEquals(AudioManager.Instance, null),
                "Releasing the destroyed owner must clear the CLR singleton reference.");
        }

        [Test]
        public void ReleaseNonOwner_DoesNotClearSingletonReference()
        {
            AudioManager owner = managerObject.GetComponent<AudioManager>();
            var otherObject = new GameObject("AudioBankRegistryOwnershipTests.NonOwner");
            otherObject.SetActive(false);
            AudioManager other = otherObject.AddComponent<AudioManager>();

            try
            {
                AudioManager.ReleaseInstance(null);
                AudioManager.ReleaseInstance(other);

                Assert.IsTrue(
                    ReferenceEquals(AudioManager.Instance, owner),
                    "Releasing null or a non-owner manager must preserve the active singleton.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(otherObject);
            }
        }

        [Test]
        public void ReleaseManager_BankUnloadCallbackCannotInstallReplacementInstance()
        {
            AudioParameter parameter = CreateObject<AudioParameter>(
                "AudioBankRegistryTests.TeardownParameter");
            AudioBank bank = CreateBank("AudioBankRegistryTests.TeardownBank", parameter);
            AudioManager replacement = null;
            bool callbackInvoked = false;
            bool replacementWasRejected = false;

            bankUnloadedHandler = unloadedBank =>
            {
                if (!ReferenceEquals(unloadedBank, bank)) return;

                callbackInvoked = true;
                var replacementObject = new GameObject(
                    "AudioBankRegistryOwnershipTests.TeardownReplacement");
                replacementObject.SetActive(false);
                replacement = replacementObject.AddComponent<AudioManager>();

                System.Reflection.MethodInfo awakeMethod = typeof(AudioManager).GetMethod(
                    "Awake",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                awakeMethod.Invoke(replacement, null);
                replacementWasRejected = replacement == null;
            };
            AudioManager.OnBankUnloaded += bankUnloadedHandler;

            AudioManager.LoadBank(bank);
            AudioManager.ReleaseInstance(managerObject.GetComponent<AudioManager>());

            Assert.IsTrue(callbackInvoked, "Cleanup must notify loaded banks.");
            Assert.IsTrue(replacementWasRejected, "Teardown must reject a replacement manager.");
            Assert.IsTrue(
                ReferenceEquals(AudioManager.Instance, null),
                "A teardown callback must not replace the singleton manager.");
        }

        [Test]
        public void LoadBank_SameInstanceTwice_IsIdempotentAndNotifiesOnceOnUnload()
        {
            AudioParameter parameter = CreateObject<AudioParameter>("AudioBankRegistryTests.IdempotentParameter");
            AudioBank bank = CreateBank("AudioBankRegistryTests.IdempotentBank", parameter);
            int unloadNotificationCount = 0;

            bankUnloadedHandler = unloadedBank =>
            {
                if (ReferenceEquals(unloadedBank, bank))
                {
                    unloadNotificationCount++;
                }
            };
            AudioManager.OnBankUnloaded += bankUnloadedHandler;

            AudioManager.LoadBank(bank);
            AudioManager.LoadBank(bank);

            Assert.AreEqual(1, AudioManager.GetLoadedBankCount());
            Assert.AreSame(parameter, AudioManager.GetParameterByName(parameter.name));

            AudioManager.UnloadBank(bank);

            Assert.AreEqual(0, AudioManager.GetLoadedBankCount());
            Assert.IsNull(AudioManager.GetParameterByName(parameter.name));
            Assert.AreEqual(1, unloadNotificationCount);

            AudioManager.UnloadBank(bank);

            Assert.AreEqual(1, unloadNotificationCount);
        }

        [Test]
        public void UnloadBank_OverwritingBank_RestoresPreviousParameterAndStateGroup()
        {
            AudioParameter originalParameter = CreateObject<AudioParameter>(CollisionParameterName);
            AudioStateGroup originalStateGroup = CreateObject<AudioStateGroup>(CollisionStateGroupName);
            AudioBank originalBank = CreateBank(
                "AudioBankRegistryTests.OriginalBank",
                originalParameter,
                originalStateGroup);

            AudioParameter overridingParameter = CreateObject<AudioParameter>(CollisionParameterName);
            AudioStateGroup overridingStateGroup = CreateObject<AudioStateGroup>(CollisionStateGroupName);
            AudioBank overridingBank = CreateBank(
                "AudioBankRegistryTests.OverridingBank",
                overridingParameter,
                overridingStateGroup);

            AudioManager.LoadBank(originalBank);
            AudioManager.LoadBank(overridingBank, overwriteExisting: true);

            Assert.AreSame(overridingParameter, AudioManager.GetParameterByName(CollisionParameterName));
            Assert.AreSame(overridingStateGroup, AudioManager.GetStateGroupByName(CollisionStateGroupName));

            AudioManager.UnloadBank(overridingBank);

            Assert.AreSame(originalParameter, AudioManager.GetParameterByName(CollisionParameterName));
            Assert.AreSame(originalStateGroup, AudioManager.GetStateGroupByName(CollisionStateGroupName));

            AudioManager.UnloadBank(originalBank);

            Assert.IsNull(AudioManager.GetParameterByName(CollisionParameterName));
            Assert.IsNull(AudioManager.GetStateGroupByName(CollisionStateGroupName));
        }

        [Test]
        public void UnloadBank_SharedParameter_RemainsUntilLastOwnerUnloads()
        {
            AudioParameter sharedParameter = CreateObject<AudioParameter>(
                "AudioBankRegistryTests.ReferenceCountedParameter");
            AudioBank firstBank = CreateBank("AudioBankRegistryTests.FirstOwner", sharedParameter);
            AudioBank secondBank = CreateBank("AudioBankRegistryTests.SecondOwner", sharedParameter);

            AudioManager.LoadBank(firstBank);
            AudioManager.LoadBank(secondBank);

            AudioManager.UnloadBank(firstBank);

            Assert.AreSame(sharedParameter, AudioManager.GetParameterByName(sharedParameter.name));
            Assert.AreEqual(1, AudioManager.GetLoadedBankCount());

            AudioManager.UnloadBank(secondBank);

            Assert.IsNull(AudioManager.GetParameterByName(sharedParameter.name));
            Assert.AreEqual(0, AudioManager.GetLoadedBankCount());
        }

        [Test]
        public void UnloadBank_AfterBankListsChange_UsesLoadTimeSnapshot()
        {
            AudioParameter loadedParameter = CreateObject<AudioParameter>(
                "AudioBankRegistryTests.SnapshottedParameter");
            AudioStateGroup loadedStateGroup = CreateObject<AudioStateGroup>(
                "AudioBankRegistryTests.SnapshottedStateGroup");
            AudioBank bank = CreateBank(
                "AudioBankRegistryTests.MutableBank",
                loadedParameter,
                loadedStateGroup);

            AudioParameter replacementParameter = CreateObject<AudioParameter>(
                "AudioBankRegistryTests.ReplacementParameter");
            AudioStateGroup replacementStateGroup = CreateObject<AudioStateGroup>(
                "AudioBankRegistryTests.ReplacementStateGroup");

            AudioManager.LoadBank(bank);

            bank.EditorParameters.Clear();
            bank.EditorParameters.Add(replacementParameter);
            bank.EditorStateGroups.Clear();
            bank.EditorStateGroups.Add(replacementStateGroup);

            AudioManager.UnloadBank(bank);

            Assert.IsNull(AudioManager.GetParameterByName(loadedParameter.name));
            Assert.IsNull(AudioManager.GetStateGroupByName(loadedStateGroup.name));
            Assert.IsNull(AudioManager.GetParameterByName(replacementParameter.name));
            Assert.IsNull(AudioManager.GetStateGroupByName(replacementStateGroup.name));
            Assert.AreEqual(0, AudioManager.GetLoadedBankCount());
        }

        private AudioBank CreateBank(string name, AudioParameter parameter)
        {
            AudioBank bank = CreateObject<AudioBank>(name);
            bank.EditorParameters.Add(parameter);
            banks.Add(bank);
            return bank;
        }

        private AudioBank CreateBank(
            string name,
            AudioParameter parameter,
            AudioStateGroup stateGroup)
        {
            AudioBank bank = CreateObject<AudioBank>(name);
            bank.EditorParameters.Add(parameter);
            bank.EditorStateGroups.Add(stateGroup);
            banks.Add(bank);
            return bank;
        }

        private T CreateObject<T>(string name) where T : ScriptableObject
        {
            T createdObject = ScriptableObject.CreateInstance<T>();
            createdObject.name = name;
            createdObject.hideFlags = HideFlags.DontSave;
            createdObjects.Add(createdObject);
            return createdObject;
        }
    }
}
