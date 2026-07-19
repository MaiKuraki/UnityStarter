using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace CycloneGames.Foundation2D.Tests.Editor
{
    public sealed class Foundation2DShaderTests
    {
        private const string SpriteShaderPath =
            "Assets/ThirdParty/CycloneGames/CycloneGames.Foundation2D/Shader/SpriteFlipbookRemap.shader";

        private const string UiShaderPath =
            "Assets/ThirdParty/CycloneGames/CycloneGames.Foundation2D/Shader/UIFlipbookRemap.shader";

        private static readonly string[] RequiredSpriteProperties =
        {
            "_MainTex",
            "_AlphaTex",
            "_EnableExternalAlpha",
            "_Color",
            "_RendererColor",
            "_Flip",
            "PixelSnap",
            "_FlipbookBaseRect",
            "_FlipbookTargetRect",
        };

        [Test]
        public void SpriteFlipbookShader_ImportsAndExposesRendererContract()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(SpriteShaderPath);
            Assert.That(shader, Is.Not.Null, "The SpriteRenderer flipbook shader could not be loaded.");
            Assert.That(shader.name, Is.EqualTo("Sprites/FlipbookRemap"));

            ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
            StringBuilder compilerErrors = new();
            for (int i = 0; i < messages.Length; i++)
            {
                ShaderMessage message = messages[i];
                if (message.severity == ShaderCompilerMessageSeverity.Error)
                {
                    compilerErrors
                        .Append(message.file)
                        .Append('(')
                        .Append(message.line)
                        .Append("): ")
                        .AppendLine(message.message);
                }
            }

            Assert.That(
                ShaderUtil.ShaderHasError(shader),
                Is.False,
                compilerErrors.Length == 0
                    ? "Unity reported a shader compiler error without a detailed message."
                    : compilerErrors.ToString());

            Material material = new(shader);
            try
            {
                for (int i = 0; i < RequiredSpriteProperties.Length; i++)
                {
                    string propertyName = RequiredSpriteProperties[i];
                    Assert.That(
                        material.HasProperty(propertyName),
                        Is.True,
                        "Required SpriteRenderer shader property is missing: " + propertyName);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [TestCase(SpriteShaderPath)]
        [TestCase(UiShaderPath)]
        public void FlipbookShaderSource_UsesValidUtf8AndOneLineEndingStyle(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            Assert.That(projectRoot, Is.Not.Null, "The Unity project root could not be resolved.");

            string fullPath = Path.Combine(
                projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
            byte[] bytes = File.ReadAllBytes(fullPath);

            bool hasUtf8Bom = bytes.Length >= 3 &&
                              bytes[0] == 0xEF &&
                              bytes[1] == 0xBB &&
                              bytes[2] == 0xBF;
            Assert.That(hasUtf8Bom, Is.False, assetPath + " must use UTF-8 without BOM.");
            Assert.DoesNotThrow(
                () => new UTF8Encoding(false, true).GetString(bytes),
                assetPath + " contains invalid UTF-8.");

            bool hasCrlf = false;
            bool hasLf = false;
            bool hasBareCr = false;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == '\r')
                {
                    if (i + 1 < bytes.Length && bytes[i + 1] == '\n')
                    {
                        hasCrlf = true;
                        i++;
                    }
                    else
                    {
                        hasBareCr = true;
                    }
                }
                else if (bytes[i] == '\n')
                {
                    hasLf = true;
                }
            }

            Assert.That(hasBareCr, Is.False, assetPath + " contains a bare carriage return.");
            Assert.That(
                hasCrlf && hasLf,
                Is.False,
                assetPath + " mixes CRLF and LF line endings.");
        }
    }
}
