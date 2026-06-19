using System;
using System.Collections.Generic;

using CycloneGames.DataTable;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayTags.Integrations.DataTable
{
    /// <summary>
    /// Registers gameplay tags from a typed DataTable, typically generated from Excel by Luban.
    /// Use this source when one row represents one tag definition.
    /// </summary>
    public sealed class GameplayTagDataTableSource<TRow> : IGameplayTagSource where TRow : IDataRow
    {
        private readonly IReadOnlyList<TRow> _rows;
        private readonly Func<TRow, string> _getName;
        private readonly Func<TRow, string> _getDescription;
        private readonly Func<TRow, GameplayTagFlags> _getFlags;
        private readonly Func<TRow, bool> _isEnabled;

        public string Name { get; }

        public GameplayTagDataTableSource(
            string sourceName,
            IDataTable<TRow> table,
            Func<TRow, string> getName,
            Func<TRow, string> getDescription = null,
            Func<TRow, GameplayTagFlags> getFlags = null,
            Func<TRow, bool> isEnabled = null)
            : this(sourceName, table?.All, getName, getDescription, getFlags, isEnabled)
        { }

        public GameplayTagDataTableSource(
            string sourceName,
            IReadOnlyList<TRow> rows,
            Func<TRow, string> getName,
            Func<TRow, string> getDescription = null,
            Func<TRow, GameplayTagFlags> getFlags = null,
            Func<TRow, bool> isEnabled = null)
        {
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                throw new ArgumentException("Gameplay tag data table source name cannot be empty.", nameof(sourceName));
            }

            Name = sourceName;
            _rows = rows ?? throw new ArgumentNullException(nameof(rows));
            _getName = getName ?? throw new ArgumentNullException(nameof(getName));
            _getDescription = getDescription ?? (static _ => string.Empty);
            _getFlags = getFlags ?? (static _ => GameplayTagFlags.None);
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

                context.RegisterTag(
                    _getName(row),
                    _getDescription(row) ?? string.Empty,
                    _getFlags(row),
                    this);
            }
        }
    }
}
