namespace TBHStats.App.ViewModels;

/// <summary>
/// Элемент списка этапов для экрана графиков (US3, FR-015).
/// Используется в ComboBox (DisplayMemberPath="Label").
/// </summary>
public sealed class ChartsStageOption
{
    /// <summary>FK → <see cref="TBHStats.Core.Models.Stage.Id"/>.</summary>
    public int StageId { get; init; }

    /// <summary>Отображаемая метка, например «1-5 Nightmare».</summary>
    public string Label { get; init; } = string.Empty;

    /// <inheritdoc/>
    public override string ToString() => Label;
}
