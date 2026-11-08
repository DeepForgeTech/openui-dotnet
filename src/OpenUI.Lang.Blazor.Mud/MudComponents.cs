namespace OpenUI.Lang.Blazor.Mud;

/// <summary>
/// The component source for this package: register it with <c>AddOpenUIComponents&lt;MudComponents&gt;()</c>. The host also calls
/// <c>AddMudServices</c> and puts the MudBlazor providers in its layout, as for any MudBlazor app. Each section is one group of
/// the definitions, in its order.
/// </summary>
public sealed class MudComponents() : LibraryDefinition("openui-mudblazor", MudDefinitions.Taught, MudDefinitions.Items, MudDefinitions.Implement, views => views

        // Layout
        .Add<StackView>("Stack")
        .Add<TabsView>("Tabs")
        .Add<AccordionView>("Accordion")
        .Add<StepsView>("Steps")
        .Add<CarouselView>("Carousel")
        .Add<SeparatorView>("Separator")
        .Add<ModalView>("Modal")

        // Content
        .Add<CardView>("Card")
        .Add<CardHeaderView>("CardHeader")
        .Add<TextContentView>("TextContent")
        .Add<MarkDownRendererView>("MarkDownRenderer")
        .Add<CalloutView>("Callout")
        .Add<TextCalloutView>("TextCallout")
        .Add<ImageView>("Image")
        .Add<ImageBlockView>("ImageBlock")
        .Add<ImageGalleryView>("ImageGallery")
        .Add<CodeBlockView>("CodeBlock")
        .Add<InlineHeaderView>("InlineHeader")

        // Tables
        .Add<TableView>("Table")
        .Add<EditableTableView>("EditableTable")

        // Charts (2D)
        .Add<BarChartView>("BarChart")
        .Add<LineChartView>("LineChart")
        // Mud has no filled area, so this draws the line and leaves the area under it empty.
        .Add<LineChartView>("AreaChart")
        .Add<RadarChartView>("RadarChart")
        .Add<HorizontalBarChartView>("HorizontalBarChart")

        // Charts (1D)
        .Add<PieChartView>("PieChart")
        .Add<RadialChartView>("RadialChart")
        .Add<SingleStackedBarChartView>("SingleStackedBarChart")

        // Charts (Scatter)
        .Add<ScatterChartView>("ScatterChart")

        // Forms
        .Add<FormView>("Form")
        .Add<FormControlView>("FormControl")
        .Add<LabelView>("Label")
        .Add<InputView>("Input")
        .Add<TextAreaView>("TextArea")
        .Add<SelectView>("Select")
        .Add<DatePickerView>("DatePicker")
        .Add<SliderView>("Slider")
        .Add<CheckBoxGroupView>("CheckBoxGroup")
        .Add<RadioGroupView>("RadioGroup")
        .Add<SwitchGroupView>("SwitchGroup")
        .Add<ChipsView>("Chips")
        .Add<OptionCardsView>("OptionCards")

        // Buttons
        .Add<ButtonView>("Button")
        .Add<ButtonsView>("Buttons")
        .Add<IconButtonView>("IconButton")

        // Data Display
        .Add<TagBlockView>("TagBlock")
        .Add<TagView>("Tag")
        .Add<IconView>("Icon")
        .Add<EntityListView>("EntityList")
        .Add<ListBlockView>("ListBlock")

        // Cards
        .Add<SnippetCardBlockView>("SnippetCardBlock")
        .Add<OverviewCardBlockView>("OverviewCardBlock")
        .Add<ContextCardBlockView>("ContextCardBlock")
        .Add<CompositeCardBlockView>("CompositeCardBlock")
        .Add<VisualCardBlockView>("VisualCardBlock")
        .Add<TextView>("Text")
        .Add<BoldTextView>("BoldText")
        .Add<IconTextView>("IconText")
        .Add<ImageTextView>("ImageText")
        .Add<ImageTextLargeView>("ImageTextLarge")
        .Add<MetricIndicatorInlineView>("MetricIndicatorInline")
        .Add<MetricIndicatorWithStrikethroughView>("MetricIndicatorWithStrikethrough")

        // Appearance
        .Add<ThemeSwitcherView>("ThemeSwitcher")
);
