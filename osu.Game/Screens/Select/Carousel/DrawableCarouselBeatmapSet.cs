﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Collections;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Rulesets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Select.Carousel
{
    public class DrawableCarouselBeatmapSet : DrawableCarouselItem, IHasContextMenu
    {
        public const float HEIGHT = MAX_HEIGHT;

        private Action<BeatmapSetInfo> restoreHiddenRequested;
        private Action<int> viewDetails;

        [Resolved(CanBeNull = true)]
        private DialogOverlay dialogOverlay { get; set; }

        [Resolved(CanBeNull = true)]
        private CollectionManager collectionManager { get; set; }

        [Resolved(CanBeNull = true)]
        private ManageCollectionsDialog manageCollectionsDialog { get; set; }

        public override IEnumerable<DrawableCarouselItem> ChildItems => beatmapContainer?.Children ?? base.ChildItems;

        private BeatmapSetInfo beatmapSet => (Item as CarouselBeatmapSet)?.BeatmapSet;

        private Container<DrawableCarouselItem> beatmapContainer;
        private Bindable<CarouselItemState> beatmapSetState;

        [Resolved]
        private BeatmapManager manager { get; set; }

        protected override void FreeAfterUse()
        {
            base.FreeAfterUse();
            Item = null;
        }

        [BackgroundDependencyLoader(true)]
        private void load(BeatmapSetOverlay beatmapOverlay)
        {
            restoreHiddenRequested = s => s.Beatmaps.ForEach(manager.Restore);

            if (beatmapOverlay != null)
                viewDetails = beatmapOverlay.FetchAndShowBeatmapSet;

            // TODO: temporary. we probably want to *not* inherit DrawableCarouselItem for this class, but only the above header portion.
            AddRangeInternal(new Drawable[]
            {
                beatmapContainer = new Container<DrawableCarouselItem>
                {
                    X = 50,
                    Y = MAX_HEIGHT,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                },
            });
        }

        protected override void UpdateItem()
        {
            base.UpdateItem();

            beatmapContainer.Clear();
            beatmapSetState?.UnbindAll();

            if (Item == null)
                return;

            Content.Children = new Drawable[]
            {
                new DelayedLoadUnloadWrapper(() =>
                {
                    var background = new PanelBackground(manager.GetWorkingBeatmap(beatmapSet.Beatmaps.FirstOrDefault()))
                    {
                        RelativeSizeAxes = Axes.Both,
                    };

                    background.OnLoadComplete += d => d.FadeInFromZero(1000, Easing.OutQuint);

                    return background;
                }, 300, 5000),
                new DelayedLoadUnloadWrapper(() =>
                {
                    var mainFlow = new FillFlowContainer
                    {
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding { Top = 5, Left = 18, Right = 10, Bottom = 10 },
                        AutoSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Text = new LocalisedString((beatmapSet.Metadata.TitleUnicode, beatmapSet.Metadata.Title)),
                                Font = OsuFont.GetFont(weight: FontWeight.Bold, size: 22, italics: true),
                                Shadow = true,
                            },
                            new OsuSpriteText
                            {
                                Text = new LocalisedString((beatmapSet.Metadata.ArtistUnicode, beatmapSet.Metadata.Artist)),
                                Font = OsuFont.GetFont(weight: FontWeight.SemiBold, size: 17, italics: true),
                                Shadow = true,
                            },
                            new FillFlowContainer
                            {
                                Direction = FillDirection.Horizontal,
                                AutoSizeAxes = Axes.Both,
                                Margin = new MarginPadding { Top = 5 },
                                Children = new Drawable[]
                                {
                                    new BeatmapSetOnlineStatusPill
                                    {
                                        Origin = Anchor.CentreLeft,
                                        Anchor = Anchor.CentreLeft,
                                        Margin = new MarginPadding { Right = 5 },
                                        TextSize = 11,
                                        TextPadding = new MarginPadding { Horizontal = 8, Vertical = 2 },
                                        Status = beatmapSet.Status
                                    },
                                    new FillFlowContainer<DifficultyIcon>
                                    {
                                        AutoSizeAxes = Axes.Both,
                                        Spacing = new Vector2(3),
                                        ChildrenEnumerable = getDifficultyIcons(),
                                    },
                                }
                            }
                        }
                    };

                    mainFlow.OnLoadComplete += d => d.FadeInFromZero(1000, Easing.OutQuint);

                    return mainFlow;
                }, 100, 5000)
            };

            beatmapSetState = Item.State.GetBoundCopy();
            beatmapSetState.BindValueChanged(setSelected, true);
        }

        private void setSelected(ValueChangedEvent<CarouselItemState> selected)
        {
            BorderContainer.MoveToX(selected.NewValue == CarouselItemState.Selected ? -100 : 0, 500, Easing.OutExpo);

            switch (selected.NewValue)
            {
                default:
                    foreach (var beatmap in beatmapContainer)
                        beatmap.FadeOut(50).Expire();
                    break;

                case CarouselItemState.Selected:

                    var carouselBeatmapSet = (CarouselBeatmapSet)Item;

                    // ToArray() in this line is required due to framework oversight: https://github.com/ppy/osu-framework/pull/3929
                    LoadComponentsAsync(carouselBeatmapSet.Children.Select(c => c.CreateDrawableRepresentation()).ToArray(), loaded =>
                    {
                        // make sure the pooled target hasn't changed.
                        if (carouselBeatmapSet != Item)
                            return;

                        float yPos = 0;

                        foreach (var item in loaded)
                        {
                            item.Y = yPos;
                            yPos += item.Item.TotalHeight;
                        }

                        beatmapContainer.ChildrenEnumerable = loaded;
                    });

                    break;
            }
        }

        private const int maximum_difficulty_icons = 18;

        private IEnumerable<DifficultyIcon> getDifficultyIcons()
        {
            var beatmaps = ((CarouselBeatmapSet)Item).Beatmaps.ToList();

            return beatmaps.Count > maximum_difficulty_icons
                ? (IEnumerable<DifficultyIcon>)beatmaps.GroupBy(b => b.Beatmap.Ruleset).Select(group => new FilterableGroupedDifficultyIcon(group.ToList(), group.Key))
                : beatmaps.Select(b => new FilterableDifficultyIcon(b));
        }

        public MenuItem[] ContextMenuItems
        {
            get
            {
                List<MenuItem> items = new List<MenuItem>();

                if (Item.State.Value == CarouselItemState.NotSelected)
                    items.Add(new OsuMenuItem("Expand", MenuItemType.Highlighted, () => Item.State.Value = CarouselItemState.Selected));

                if (beatmapSet.OnlineBeatmapSetID != null && viewDetails != null)
                    items.Add(new OsuMenuItem("Details...", MenuItemType.Standard, () => viewDetails(beatmapSet.OnlineBeatmapSetID.Value)));

                if (collectionManager != null)
                {
                    var collectionItems = collectionManager.Collections.Select(createCollectionMenuItem).ToList();
                    if (manageCollectionsDialog != null)
                        collectionItems.Add(new OsuMenuItem("Manage...", MenuItemType.Standard, manageCollectionsDialog.Show));

                    items.Add(new OsuMenuItem("Collections") { Items = collectionItems });
                }

                if (beatmapSet.Beatmaps.Any(b => b.Hidden))
                    items.Add(new OsuMenuItem("Restore all hidden", MenuItemType.Standard, () => restoreHiddenRequested(beatmapSet)));

                if (dialogOverlay != null)
                    items.Add(new OsuMenuItem("Delete...", MenuItemType.Destructive, () => dialogOverlay.Push(new BeatmapDeleteDialog(beatmapSet))));
                return items.ToArray();
            }
        }

        private MenuItem createCollectionMenuItem(BeatmapCollection collection)
        {
            TernaryState state;

            var countExisting = beatmapSet.Beatmaps.Count(b => collection.Beatmaps.Contains(b));

            if (countExisting == beatmapSet.Beatmaps.Count)
                state = TernaryState.True;
            else if (countExisting > 0)
                state = TernaryState.Indeterminate;
            else
                state = TernaryState.False;

            return new TernaryStateMenuItem(collection.Name.Value, MenuItemType.Standard, s =>
            {
                foreach (var b in beatmapSet.Beatmaps)
                {
                    switch (s)
                    {
                        case TernaryState.True:
                            if (collection.Beatmaps.Contains(b))
                                continue;

                            collection.Beatmaps.Add(b);
                            break;

                        case TernaryState.False:
                            collection.Beatmaps.Remove(b);
                            break;
                    }
                }
            })
            {
                State = { Value = state }
            };
        }

        private class PanelBackground : BufferedContainer
        {
            public PanelBackground(WorkingBeatmap working)
            {
                CacheDrawnFrameBuffer = true;
                RedrawOnScale = false;

                Children = new Drawable[]
                {
                    new BeatmapBackgroundSprite(working)
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        FillMode = FillMode.Fill,
                    },
                    new FillFlowContainer
                    {
                        Depth = -1,
                        RelativeSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        // This makes the gradient not be perfectly horizontal, but diagonal at a ~40° angle
                        Shear = new Vector2(0.8f, 0),
                        Alpha = 0.5f,
                        Children = new[]
                        {
                            // The left half with no gradient applied
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = Color4.Black,
                                Width = 0.4f,
                            },
                            // Piecewise-linear gradient with 3 segments to make it appear smoother
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = ColourInfo.GradientHorizontal(Color4.Black, new Color4(0f, 0f, 0f, 0.9f)),
                                Width = 0.05f,
                            },
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = ColourInfo.GradientHorizontal(new Color4(0f, 0f, 0f, 0.9f), new Color4(0f, 0f, 0f, 0.1f)),
                                Width = 0.2f,
                            },
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = ColourInfo.GradientHorizontal(new Color4(0f, 0f, 0f, 0.1f), new Color4(0, 0, 0, 0)),
                                Width = 0.05f,
                            },
                        }
                    },
                };
            }
        }

        public class FilterableDifficultyIcon : DifficultyIcon
        {
            private readonly BindableBool filtered = new BindableBool();

            public bool IsFiltered => filtered.Value;

            public readonly CarouselBeatmap Item;

            public FilterableDifficultyIcon(CarouselBeatmap item)
                : base(item.Beatmap)
            {
                filtered.BindTo(item.Filtered);
                filtered.ValueChanged += isFiltered => Schedule(() => this.FadeTo(isFiltered.NewValue ? 0.1f : 1, 100));
                filtered.TriggerChange();

                Item = item;
            }

            protected override bool OnClick(ClickEvent e)
            {
                Item.State.Value = CarouselItemState.Selected;
                return true;
            }
        }

        public class FilterableGroupedDifficultyIcon : GroupedDifficultyIcon
        {
            public readonly List<CarouselBeatmap> Items;

            public FilterableGroupedDifficultyIcon(List<CarouselBeatmap> items, RulesetInfo ruleset)
                : base(items.Select(i => i.Beatmap).ToList(), ruleset, Color4.White)
            {
                Items = items;

                foreach (var item in items)
                    item.Filtered.BindValueChanged(_ => Scheduler.AddOnce(updateFilteredDisplay));

                updateFilteredDisplay();
            }

            protected override bool OnClick(ClickEvent e)
            {
                Items.First().State.Value = CarouselItemState.Selected;
                return true;
            }

            private void updateFilteredDisplay()
            {
                // for now, fade the whole group based on the ratio of hidden items.
                this.FadeTo(1 - 0.9f * ((float)Items.Count(i => i.Filtered.Value) / Items.Count), 100);
            }
        }
    }
}
