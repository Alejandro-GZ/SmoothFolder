using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SmoothFolder.Services;

public static class IosContextMenuService
{
    private static readonly TimeSpan OpenDuration =
        TimeSpan.FromMilliseconds(
            125);

    public static ContextMenu Create()
    {
        var menu =
            new ContextMenu
            {
                Style =
                    RequireStyle(
                        "IosContextMenuStyle"),
                Placement =
                    PlacementMode.MousePoint,
                HorizontalOffset =
                    2,
                VerticalOffset =
                    2,
                Opacity =
                    0,
                RenderTransformOrigin =
                    new Point(
                        0.5,
                        0.0),
                RenderTransform =
                    new ScaleTransform(
                        0.965,
                        0.965)
            };

        menu.Opened +=
            (_, _) =>
                AnimateOpen(
                    menu);

        return menu;
    }

    public static MenuItem Item(
        string label,
        Action action,
        bool destructive = false)
    {
        var item =
            new MenuItem
            {
                Header =
                    label,
                Style =
                    RequireStyle(
                        "IosContextMenuItemStyle"),
                Tag =
                    destructive
                        ? "Destructive"
                        : null
            };

        item.Click +=
            (_, _) =>
                action();

        return item;
    }

    public static Separator Separator() =>
        new()
        {
            Style =
                RequireStyle(
                    "IosContextMenuSeparatorStyle")
        };

    private static void AnimateOpen(
        ContextMenu menu)
    {
        menu.BeginAnimation(
            UIElement.OpacityProperty,
            null);

        menu.Opacity =
            0;

        var scale =
            menu.RenderTransform as
                ScaleTransform;

        if (scale is null)
        {
            scale =
                new ScaleTransform();

            menu.RenderTransform =
                scale;
        }

        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            null);

        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            null);

        scale.ScaleX =
            0.965;

        scale.ScaleY =
            0.965;

        var ease =
            new CubicEase
            {
                EasingMode =
                    EasingMode.EaseOut
            };

        menu.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(
                0,
                1,
                OpenDuration)
            {
                EasingFunction =
                    ease
            });

        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(
                0.965,
                1,
                OpenDuration)
            {
                EasingFunction =
                    ease
            });

        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(
                0.965,
                1,
                OpenDuration)
            {
                EasingFunction =
                    ease
            });
    }

    private static Style RequireStyle(
        string resourceKey)
    {
        if (Application.Current.TryFindResource(
                resourceKey) is Style style)
        {
            return style;
        }

        throw new InvalidOperationException(
            $"SmoothFolder UI resource '{resourceKey}' was not found.");
    }
}
