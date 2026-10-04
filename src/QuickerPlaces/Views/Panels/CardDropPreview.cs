using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace QuickerPlaces.Views.Panels;

/// <summary>A shared insertion preview for stacked or wrapping cards. Hit testing and dropping use the same target.</summary>
internal sealed class CardDropPreview<T> where T : class
{
    private readonly ItemsControl _items;
    private InsertionAdorner? _adorner;
    private AdornerLayer? _layer;

    public CardDropPreview(ItemsControl items) => _items = items;

    public void Drag(FrameworkElement source, T item)
    {
        var opacity = source.Opacity;
        source.SetCurrentValue(UIElement.OpacityProperty, 0.45);
        try
        {
            DragDrop.DoDragDrop(source, new DataObject(typeof(T), item), DragDropEffects.Move);
        }
        finally
        {
            source.SetCurrentValue(UIElement.OpacityProperty, opacity);
            Clear();
        }
    }

    public bool Contains(Point point) => new Rect(_items.RenderSize).Contains(point);

    public CardDropTarget<T>? FindTarget(Point point, T dragged, bool vertical)
    {
        var from = _items.Items.IndexOf(dragged);
        if (from < 0 || !Contains(point))
            return null;

        CardDropTarget<T>? nearest = null;
        var distance = double.PositiveInfinity;
        for (var index = 0; index < _items.Items.Count; index++)
        {
            if (_items.Items[index] is not T item ||
                _items.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container)
                continue;

            // An ItemsControl's presenter includes the favourite button's margins;
            // measure the button itself so the marker sits in the gap beside it.
            var card = container is ContentPresenter && VisualTreeHelper.GetChildrenCount(container) > 0 &&
                VisualTreeHelper.GetChild(container, 0) is FrameworkElement content ? content : container;
            if (card.Visibility != Visibility.Visible || card.ActualWidth <= 0 || card.ActualHeight <= 0)
                continue;
            var bounds = card.TransformToAncestor(_items).TransformBounds(new Rect(card.RenderSize));
            if (!bounds.IntersectsWith(new Rect(_items.RenderSize)))
                continue;

            var dx = Math.Max(0, Math.Max(bounds.Left - point.X, point.X - bounds.Right));
            var dy = Math.Max(0, Math.Max(bounds.Top - point.Y, point.Y - bounds.Bottom));
            var candidateDistance = dx * dx + dy * dy;
            if (candidateDistance >= distance)
                continue;

            distance = candidateDistance;
            var after = vertical ? point.Y > bounds.Top + bounds.Height / 2 : point.X > bounds.Left + bounds.Width / 2;
            var insertion = index + (after ? 1 : 0);
            var targetIndex = insertion - (from < insertion ? 1 : 0);
            nearest = new CardDropTarget<T>(item, targetIndex, after, bounds);
        }

        // Both halves of the dragged card, and its adjacent unchanged slots,
        // leave it where it is: don't show a misleading move preview there.
        return nearest is { } target && target.TargetIndex != from ? target : null;
    }

    public void Update(Point point, T dragged, bool vertical)
    {
        if (FindTarget(point, dragged, vertical) is not { } target)
        {
            Clear();
            return;
        }

        if (_adorner is null)
        {
            _layer = AdornerLayer.GetAdornerLayer(_items);
            if (_layer is null)
                return;
            _adorner = new InsertionAdorner(_items);
            _layer.Add(_adorner);
        }
        _adorner.Show(target.Bounds, vertical, target.After);
    }

    public void Clear()
    {
        if (_adorner is not null)
            _layer?.Remove(_adorner);
        _adorner = null;
        _layer = null;
    }

    private sealed class InsertionAdorner : Adorner
    {
        private Rect _bounds;
        private bool _vertical;
        private bool _after;

        public InsertionAdorner(UIElement element) : base(element) => IsHitTestVisible = false;

        public void Show(Rect bounds, bool vertical, bool after)
        {
            _bounds = bounds;
            _vertical = vertical;
            _after = after;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var brush = (AdornedElement as FrameworkElement)?.TryFindResource("Highlight.Text") as Brush ?? Brushes.DodgerBlue;
            var soft = (AdornedElement as FrameworkElement)?.TryFindResource("Highlight.Soft") as Brush ?? Brushes.Transparent;
            Point start, end;
            if (_vertical)
            {
                var y = Math.Clamp((_after ? _bounds.Bottom + 4 : _bounds.Top - 4), 3, Math.Max(3, AdornedElement.RenderSize.Height - 3));
                start = new Point(_bounds.Left + 3, y);
                end = new Point(Math.Max(start.X, _bounds.Right - 3), y);
                drawingContext.DrawRoundedRectangle(soft, null, new Rect(start.X - 3, y - 4, end.X - start.X + 6, 8), 4, 4);
            }
            else
            {
                var x = Math.Clamp((_after ? _bounds.Right + 4 : _bounds.Left - 4), 3, Math.Max(3, AdornedElement.RenderSize.Width - 3));
                start = new Point(x, _bounds.Top + 3);
                end = new Point(x, Math.Max(start.Y, _bounds.Bottom - 3));
                drawingContext.DrawRoundedRectangle(soft, null, new Rect(x - 4, start.Y - 3, 8, end.Y - start.Y + 6), 4, 4);
            }
            drawingContext.DrawLine(new Pen(brush, 3), start, end);
            drawingContext.DrawEllipse(brush, null, start, 3, 3);
            drawingContext.DrawEllipse(brush, null, end, 3, 3);
        }
    }
}

internal sealed record CardDropTarget<T>(T Item, int TargetIndex, bool After, Rect Bounds);
