using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Starward.Core.Games.Kuro;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Starward.Features.GameSetting;


/// <summary>
/// 下拉框里的一项。<see cref="Value"/> 为 null 是「游戏预设」，即不写入这个键。
/// </summary>
public sealed class KuroEngineTweakOptionViewModel
{

    public KuroEngineTweakOptionViewModel(string? value, string label)
    {
        Value = value;
        Label = label;
    }

    public string? Value { get; }

    public string Label { get; }

    public override string ToString() => Label;

}


/// <summary>
/// 一个调校项。值统一用字符串表示，null 表示文件里没有这个键。
/// </summary>
public sealed partial class KuroEngineTweakItemViewModel : ObservableObject
{

    internal KuroEngineTweak Tweak { get; }

    public string Title { get; }

    public string Key => Tweak.Key;

    public string Description { get; }

    public double Step => Tweak.Step;

    public bool IsChoice => Tweak.Kind is KuroEngineTweakKind.Choice;

    public bool IsNumber => Tweak.Kind is KuroEngineTweakKind.Number;

    public Visibility ChoiceVisibility => IsChoice ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NumberVisibility => IsNumber ? Visibility.Visible : Visibility.Collapsed;

    public ObservableCollection<KuroEngineTweakOptionViewModel> Options { get; } = new();

    public event EventHandler? Changed;

    private bool _suppressChanged;


    public KuroEngineTweakItemViewModel(KuroEngineTweak tweak)
    {
        Tweak = tweak;
        Title = KuroEngineTweakText.Title(tweak);
        Description = KuroEngineTweakText.Description(tweak);
        if (IsChoice)
        {
            Options.Add(new KuroEngineTweakOptionViewModel(null, KuroEngineTweakText.Ui_GameDefault));
            foreach (KuroEngineTweakOption option in tweak.Options)
            {
                Options.Add(new KuroEngineTweakOptionViewModel(option.Value, KuroEngineTweakText.Option(option)));
            }
            SelectedOption = Options[0];
        }
    }


    /// <summary>
    /// 现值，null 为游戏预设
    /// </summary>
    public string? Value
    {
        get => IsChoice ? SelectedOption?.Value : double.IsNaN(NumberValue) ? null : NumberValue.ToString(CultureInfo.InvariantCulture);
    }


    public bool IsSet => Value is not null;

    public Windows.UI.Text.FontWeight TitleWeight => IsSet ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;


    public KuroEngineTweakOptionViewModel? SelectedOption
    {
        get;
        set
        {
            // 下拉框重建项目时会短暂送来 null，忽略它，免得把值清成游戏预设
            if (value is null)
            {
                return;
            }
            if (SetProperty(ref field, value))
            {
                OnValueChanged();
            }
        }
    }


    public double NumberValue
    {
        get;
        set
        {
            if (!double.IsNaN(value))
            {
                // NumberBox 的小数步进会累积误差（0.1 + 0.2），按步进的位数修整
                value = Math.Round(value, StepDecimals);
            }
            if (field.Equals(value))
            {
                return;
            }
            field = value;
            OnPropertyChanged();
            OnValueChanged();
        }
    } = double.NaN;


    private int StepDecimals
    {
        get
        {
            string step = Tweak.Step.ToString(CultureInfo.InvariantCulture);
            int dot = step.IndexOf('.');
            return Math.Max(dot < 0 ? 0 : step.Length - dot - 1, 3);
        }
    }


    /// <summary>
    /// 从文件或预设设定值，不算玩家的改动。认不得的值也保留成一个「目前值」选项，不能悄悄丢掉。
    /// </summary>
    public void SetValue(string? value, bool raiseChanged)
    {
        _suppressChanged = !raiseChanged;
        try
        {
            if (IsChoice)
            {
                KuroEngineTweakOptionViewModel? option = value is null
                    ? Options[0]
                    : Options.FirstOrDefault(x => x.Value is not null && KuroEngineTweakCatalog.ValueEquals(x.Value, value));
                if (option is null)
                {
                    option = new KuroEngineTweakOptionViewModel(value, string.Format(KuroEngineTweakText.Ui_CurrentValue, value));
                    Options.Add(option);
                }
                SelectedOption = option;
            }
            else
            {
                NumberValue = value is not null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : double.NaN;
            }
        }
        finally
        {
            _suppressChanged = false;
        }
    }


    private void OnValueChanged()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsSet));
        OnPropertyChanged(nameof(TitleWeight));
        if (!_suppressChanged)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

}


/// <summary>
/// 一个分类，展开后列出其中的调校项
/// </summary>
public sealed partial class KuroEngineTweakCategoryViewModel : ObservableObject
{

    public string Title { get; }

    public List<KuroEngineTweakItemViewModel> Items { get; }


    public KuroEngineTweakCategoryViewModel(string category, List<KuroEngineTweakItemViewModel> items)
    {
        Title = KuroEngineTweakText.Category(category);
        Items = items;
        VisibleItems = items;
        foreach (var item in items)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(KuroEngineTweakItemViewModel.IsSet))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            };
        }
    }


    public string Summary => string.Format(KuroEngineTweakText.Ui_ModifiedCount, Items.Count(x => x.IsSet), Items.Count);


    /// <summary>
    /// 符合搜索的项目；没在搜索时就是 <see cref="Items"/>
    /// </summary>
    public List<KuroEngineTweakItemViewModel> VisibleItems { get; private set => SetProperty(ref field, value); }

    public bool IsExpanded { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 按搜索词筛选，返回是否还有符合的项目。分类名本身符合时整个分类都列出。
    /// </summary>
    public bool ApplySearch(string? query)
    {
        VisibleItems = Items.Where(x => KuroEngineTweakText.MatchesSearch(query, x.Title, x.Key, x.Description, Title)).ToList();
        return VisibleItems.Count > 0;
    }

}
