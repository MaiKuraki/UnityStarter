using System;
using System.Collections.Generic;

using CycloneGames.DataTable;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayTags.Integrations.DataTable
{
    /// <summary>
    /// Registers gameplay tags referenced by generated DataTable rows.
    /// This is useful for GameplayAbilities or GameplayEffects authored as Luban rows instead of ScriptableObjects.
    /// </summary>
    public sealed class GameplayTagDataTableReferenceSource<TRow> : IGameplayTagSource where TRow : IDataRow
    {
        private readonly IReadOnlyList<TRow> _rows;
        private readonly Func<TRow, IEnumerable<string>>[] _getTagNameCollections;
        private readonly Func<TRow, string> _getDescription;
        private readonly Func<TRow, bool> _isEnabled;

        public string Name { get; }

        public GameplayTagDataTableReferenceSource(
            string sourceName,
            IDataTable<TRow> table,
            params Func<TRow, IEnumerable<string>>[] getTagNameCollections)
            : this(sourceName, table?.All, null, null, getTagNameCollections)
        { }

        public GameplayTagDataTableReferenceSource(
            string sourceName,
            IReadOnlyList<TRow> rows,
            params Func<TRow, IEnumerable<string>>[] getTagNameCollections)
            : this(sourceName, rows, null, null, getTagNameCollections)
        { }

        public GameplayTagDataTableReferenceSource(
            string sourceName,
            IDataTable<TRow> table,
            Func<TRow, string> getDescription,
            Func<TRow, bool> isEnabled,
            params Func<TRow, IEnumerable<string>>[] getTagNameCollections)
            : this(sourceName, table?.All, getDescription, isEnabled, getTagNameCollections)
        { }

        public GameplayTagDataTableReferenceSource(
            string sourceName,
            IReadOnlyList<TRow> rows,
            Func<TRow, string> getDescription,
            Func<TRow, bool> isEnabled,
            params Func<TRow, IEnumerable<string>>[] getTagNameCollections)
        {
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                throw new ArgumentException("Gameplay tag data table source name cannot be empty.", nameof(sourceName));
            }

            if (getTagNameCollections == null || getTagNameCollections.Length == 0)
            {
                throw new ArgumentException("At least one tag collection accessor is required.", nameof(getTagNameCollections));
            }

            Name = sourceName;
            _rows = rows ?? throw new ArgumentNullException(nameof(rows));
            _getTagNameCollections = getTagNameCollections;
            _getDescription = getDescription ?? (static _ => string.Empty);
            _isEnabled = isEnabled ?? (static _ => true);
        }

        public void RegisterTags(GameplayTagRegistrationContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                TRow row = _rows[i];
                if (row == null || !_isEnabled(row))
                {
                    continue;
                }

                string description = _getDescription(row) ?? string.Empty;
                for (int accessorIndex = 0; accessorIndex < _getTagNameCollections.Length; accessorIndex++)
                {
                    RegisterTagsFromCollection(context, row, _getTagNameCollections[accessorIndex], description);
                }
            }
        }

        private void RegisterTagsFromCollection(
            GameplayTagRegistrationContext context,
            TRow row,
            Func<TRow, IEnumerable<string>> getTagNames,
            string description)
        {
            if (getTagNames == null)
            {
                return;
            }

            IEnumerable<string> tagNames = getTagNames(row);
            if (tagNames == null)
            {
                return;
            }

            if (tagNames is IReadOnlyList<string> tagNameList)
            {
                for (int i = 0; i < tagNameList.Count; i++)
                {
                    RegisterTag(context, tagNameList[i], description);
                }

                return;
            }

            foreach (string tagName in tagNames)
            {
                RegisterTag(context, tagName, description);
            }
        }

        private void RegisterTag(GameplayTagRegistrationContext context, string tagName, string description)
        {
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return;
            }

            context.RegisterTag(tagName, description, GameplayTagFlags.None, this);
        }
    }
}
