﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace uTILLIty.Controls.WPF.LazyComboBox
{
	[TemplatePart(Name = "PART_TextBox", Type = typeof (TextBox))]
	[TemplatePart(Name = "PART_ListView", Type = typeof (ListView))]
	[TemplatePart(Name = "PART_SelectedItemColumn", Type = typeof (FrameworkElement))]
	public class LazyComboBox : Selector, INotifyPropertyChanged
	{
		private ICollectionView _itemsView;
		private ListView _listView;

		private object _lookupContextTag;
		private FrameworkElement _selItemCol;
		private TextBox _textBox;

		private bool _textChangedFromCode;

		private CancellationTokenSource _token;

		static LazyComboBox()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof (LazyComboBox), new FrameworkPropertyMetadata(typeof (LazyComboBox)));
			ItemsSourceProperty.OverrideMetadata(typeof (LazyComboBox), new FrameworkPropertyMetadata(OnItemsSourceChanged));
			SelectedItemProperty.OverrideMetadata(typeof (LazyComboBox),
				new FrameworkPropertyMetadata(OnSelectedItemChanged, OnCoerceSelectedItem));
		}

		private ICollectionView ItemsView
		{
			get
			{
				if (_itemsView == null && ItemsSource != null)
				{
					var s = CollectionViewSource.GetDefaultView(ItemsSource);
					using (s.DeferRefresh())
					{
						s.SortDescriptions.Clear();
						s.GroupDescriptions.Clear();
					}
					_itemsView = s;
				}
				return _itemsView;
			}
		}

		public event PropertyChangedEventHandler PropertyChanged;
		public event EventHandler DropDownOpened;

		public override void OnApplyTemplate()
		{
			base.OnApplyTemplate();

			_selItemCol = (FrameworkElement) Template.FindName("PART_SelectedItemColumn", this);
			_selItemCol.PreviewMouseLeftButtonDown += OnSelectedItemContentClicked;

			_listView = (ListView) Template.FindName("PART_ListView", this);
			_listView.AddHandler(ScrollBar.ScrollEvent, new RoutedEventHandler(OnScrolled));
			_listView.AddHandler(SelectionChangedEvent, new RoutedEventHandler(OnListItemChanged));
			_listView.AddHandler(MouseUpEvent, new RoutedEventHandler(OnListClicked));
			_listView.AddHandler(LostFocusEvent, new RoutedEventHandler(OnListLostFocus));

			//_popup = (Popup) Template.FindName("PART_Popup", this);

			_textBox = (TextBox) Template.FindName("PART_TextBox", this);
			_textBox.PreviewKeyDown += (o, e) => OnPreviewKeyDown(e);
			_textBox.LostFocus += OnTextBoxLostFocus;
		}

		private void OnListLostFocus(object sender, RoutedEventArgs e)
		{
			IsDropDownOpen = false;
		}

		private void OnListItemChanged(object sender, RoutedEventArgs e)
		{
			//occurs on change of the current item of the view!
			var view = ItemsView;
			if (view == null)
				return;

			Debug.WriteLine($"OnListItemChanged: Setting SelectedItem to {view.CurrentItem}");
			SelectedItem = view.CurrentItem;
		}

		private void OnListClicked(object sender, RoutedEventArgs e)
		{
			var view = ItemsView;
			if (view == null)
				return;

			Debug.WriteLine($"OnListClicked: Setting SelectedItem to {view.CurrentItem}");
			SelectedItem = view.CurrentItem;
			IsDropDownOpen = false;
		}

		private void RaiseDropDownOpened()
		{
			DropDownOpened?.Invoke(this, EventArgs.Empty);
		}

		protected override void OnPreviewKeyDown(KeyEventArgs e)
		{
			var view = ItemsView;
			if (view == null)
				return;

			var key = e.Key;
			switch (key)
			{
				case Key.Home:
					IsDropDownOpen = true;
					view.MoveCurrentToFirst();
					_listView.ScrollIntoView(view.CurrentItem);
					e.Handled = true;
					break;
				case Key.End:
					IsDropDownOpen = true;
					view.MoveCurrentToLast();
					_listView.ScrollIntoView(view.CurrentItem);
					e.Handled = true;
					break;
				case Key.Down:
					IsDropDownOpen = true;
					view.MoveCurrentToNext();
					if (view.IsCurrentAfterLast)
						view.MoveCurrentToFirst();
					_listView.ScrollIntoView(view.CurrentItem);
					e.Handled = true;
					break;
				case Key.Up:
					IsDropDownOpen = true;
					view.MoveCurrentToPrevious();
					if (view.IsCurrentBeforeFirst)
						view.MoveCurrentToLast();
					_listView.ScrollIntoView(view.CurrentItem);
					e.Handled = true;
					break;
				case Key.Return:
					if (view.CurrentItem != null)
					{
						Debug.WriteLine($"OnPreviewKeyDown (RETURN): Setting SelectedItem to {view.CurrentItem}");
						SelectedItem = view.CurrentItem;
						IsDropDownOpen = false;
						e.Handled = true;
					}
					break;
				default:
					return;
			}
			e.Handled = true;
		}

		private void OnTextBoxLostFocus(object sender, RoutedEventArgs e)
		{
			IsEditing = false;
		}

		private void OnScrolled(object sender, RoutedEventArgs e)
		{
			var sb = (ScrollBar) e.OriginalSource;

			if (sb.Orientation == Orientation.Horizontal)
				return;

			Debug.WriteLine($"Scroll Position={sb.Value}, Max={sb.Maximum}");
		}

		private void OnSelectedItemContentClicked(object sender, MouseButtonEventArgs e)
		{
			if (IsEditable)
			{
				IsEditing = true;
				_textBox.Focus();
				_textBox.SelectAll();
			}
			else
			{
				IsDropDownOpen = !IsDropDownOpen;
			}
		}

		private void OnTextChanged()
		{
			if (_textChangedFromCode)
				return;
			InvokeLookupAction();
			IsDropDownOpen = true;
		}

		private void InvokeLookupAction(bool async = true)
		{
			var action = LookupAction;
			if (action != null)
			{
				try
				{
					_token?.Cancel(true);
					_token = new CancellationTokenSource();
					var input = Text;
					ListUpdating = true;
					var ctx = new LookupContext(input, _token.Token, _lookupContextTag);
					if (async)
					{
						//ConfigureAwait must be true to be back in UI thread afterward
						Task.Run(() => action.Invoke(ctx));
					}
					else
					{
						action.Invoke(ctx);
					}
					_lookupContextTag = ctx.Tag;
				}
				finally
				{
					ListUpdating = false;
				}
			}
		}

		private void UpdateTypedText(object selItem)
		{
			string text = null;
			if (selItem != null)
			{
				var pi = TryGetProperty(TextMember, selItem.GetType());
				text = pi == null ? selItem.ToString() : pi.GetValue(selItem)?.ToString();
			}
			try
			{
				_textChangedFromCode = true;
				Text = text;
				SelectedItemText = text;
			}
			finally
			{
				_textChangedFromCode = false;
			}
		}

		private PropertyInfo TryGetProperty(string propertyName, Type type)
		{
			if (string.IsNullOrEmpty(propertyName))
				return null;
			var pi = type?.GetProperty(propertyName,
				BindingFlags.Instance | BindingFlags.FlattenHierarchy | BindingFlags.IgnoreCase | BindingFlags.Public);
			return pi;
		}

		protected internal void ResetItemsView()
		{
			_itemsView = null;
			// ReSharper disable once ExplicitCallerInfoArgument
			RaisePropertyChanged(nameof(ItemsView));
		}

		[NotifyPropertyChangedInvocator]
		protected virtual void RaisePropertyChanged([CallerMemberName] string propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		#region ListUpdating Property

		public static readonly DependencyProperty ListUpdatingProperty = DependencyProperty
			.Register(nameof(ListUpdating), typeof (bool), typeof (LazyComboBox)
			/*, new FrameworkPropertyMetadata(string.Empty, OnListUpdatingChanged)*/);

		public bool ListUpdating
		{
			get { return (bool) GetValue(ListUpdatingProperty); }
			private set { SetValue(ListUpdatingProperty, value); }
		}

		#endregion

		#region DropDownButtonStyle Property

		public static readonly DependencyProperty DropDownButtonStyleProperty = DependencyProperty
			.Register("DropDownButtonStyle", typeof (Style), typeof (LazyComboBox));

		public Style DropDownButtonStyle
		{
			get { return (Style) GetValue(DropDownButtonStyleProperty); }
			set { SetValue(DropDownButtonStyleProperty, value); }
		}

		#endregion

		#region PopupBorderStyle Property

		public static readonly DependencyProperty PopupBorderStyleProperty = DependencyProperty
			.Register(nameof(PopupBorderStyle), typeof (Style), typeof (LazyComboBox)
			/*, new FrameworkPropertyMetadata(string.Empty, OnPopupBorderStyleChanged)*/);

		public Style PopupBorderStyle
		{
			get { return (Style) GetValue(PopupBorderStyleProperty); }
			set { SetValue(PopupBorderStyleProperty, value); }
		}

		#endregion

		#region ListStyle Property

		public static readonly DependencyProperty ListStyleProperty = DependencyProperty
			.Register(nameof(ListStyle), typeof (Style), typeof (LazyComboBox)
			/*, new FrameworkPropertyMetadata(string.Empty, OnListStyleChanged)*/);

		public Style ListStyle
		{
			get { return (Style) GetValue(ListStyleProperty); }
			set { SetValue(ListStyleProperty, value); }
		}

		#endregion

		#region SelectedItem Property

		private static object OnCoerceSelectedItem(DependencyObject d, object basevalue)
		{
			var t = (LazyComboBox) d;
			if (t.ItemsSource == null && basevalue != null)
			{
				t.UpdateTypedText(basevalue);
				t.InvokeLookupAction(false);
			}
			return basevalue;
		}

		private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var t = (LazyComboBox) d;
			t.UpdateTypedText(t.SelectedItem);
		}

		#endregion

		#region SelectedItemText Property

		public static readonly DependencyProperty SelectedItemTextProperty = DependencyProperty
			.Register(nameof(SelectedItemText), typeof (string), typeof (LazyComboBox)
				, new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

		private string SelectedItemText
		{
			get { return (string) GetValue(SelectedItemTextProperty); }
			set { SetValue(SelectedItemTextProperty, value); }
		}

		#endregion

		#region ItemsSource Property

		private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var t = (LazyComboBox) d;
			t.ResetItemsView();
			t.UpdateTypedText(t.SelectedItem);
		}

		#endregion

		#region LookupAction Property

		public static readonly DependencyProperty LookupActionProperty = DependencyProperty
			.Register(nameof(LookupAction), typeof (Action<LookupContext>), typeof (LazyComboBox)
			/*, new FrameworkPropertyMetadata(string.Empty, OnLookupActionChanged)*/);

		/// <summary>
		///   This action is called in a background thread to populate the <see cref="Selector.ItemsSource" /> with elements
		///   filtered by the user's input
		/// </summary>
		/// <remarks>
		///   The <see cref="LookupContext" /> provided can hold an arbitrary state object in the <see cref="LookupContext.Tag" />
		///   property, which will be passed to you on subsequent calls. Also check the
		///   <see cref="LookupContext.CancellationToken" /> frequently and abort your lookup-operation as soon as possible
		///   without setting the <see cref="Selector.ItemsSource" /> property. This operation is cancelled, if additional user-input
		///   has occured since calling the LookupAction
		/// </remarks>
		public Action<LookupContext> LookupAction
		{
			get { return (Action<LookupContext>) GetValue(LookupActionProperty); }
			set { SetValue(LookupActionProperty, value); }
		}

		#endregion

		#region IsEditable Property

		public static readonly DependencyProperty IsEditableProperty = DependencyProperty
			.Register(nameof(IsEditable), typeof (bool), typeof (LazyComboBox), new FrameworkPropertyMetadata(true));

		public bool IsEditable
		{
			get { return (bool) GetValue(IsEditableProperty); }
			set { SetValue(IsEditableProperty, value); }
		}

		#endregion

		#region TextBoxStyle Property

		public static readonly DependencyProperty TextBoxStyleProperty = DependencyProperty
			.Register(nameof(TextBoxStyle), typeof (Style), typeof (LazyComboBox)
			/*, new FrameworkPropertyMetadata(string.Empty, OnTextBoxStyleChanged)*/);

		public Style TextBoxStyle
		{
			get { return (Style) GetValue(TextBoxStyleProperty); }
			set { SetValue(TextBoxStyleProperty, value); }
		}

		#endregion

		#region TextMember Property

		public static readonly DependencyProperty TextMemberProperty = DependencyProperty
			.Register(nameof(TextMember), typeof (string), typeof (LazyComboBox)
			/*, new FrameworkPropertyMetadata(string.Empty, OnTextMemberChanged)*/);

		public string TextMember
		{
			get { return (string) GetValue(TextMemberProperty); }
			set { SetValue(TextMemberProperty, value); }
		}

		#endregion

		#region Text Property

		public static readonly DependencyProperty TextProperty = DependencyProperty
			.Register(nameof(Text), typeof (string), typeof (LazyComboBox)
				,
				new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
					OnTypedTextChanged));

		private static void OnTypedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var t = (LazyComboBox) d;
			t.OnTextChanged();
		}

		public string Text
		{
			get { return (string) GetValue(TextProperty); }
			set { SetValue(TextProperty, value); }
		}

		#endregion

		#region IsEditing Property

		public static readonly DependencyProperty IsEditingProperty = DependencyProperty
			.Register(nameof(IsEditing), typeof (bool), typeof (LazyComboBox)
			/*, new FrameworkPropertyMetadata(string.Empty, OnIsEditingChanged)*/);

		internal bool IsEditing
		{
			get { return (bool) GetValue(IsEditingProperty); }
			set { SetValue(IsEditingProperty, value); }
		}

		#endregion

		#region IsDropDownOpen Property

		public static readonly DependencyProperty IsDropDownOpenProperty = DependencyProperty
			.Register(nameof(IsDropDownOpen), typeof (bool), typeof (LazyComboBox)
				,
				new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsDropDownOpenChanged));

		private static void OnIsDropDownOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var t = (LazyComboBox) d;
			if (t.IsDropDownOpen)
			{
				if (t.ItemsSource == null)
					t.InvokeLookupAction(false);
				t.RaiseDropDownOpened();
				if (!t.IsEditing)
					Keyboard.Focus(t);
			}
		}

		private bool IsDropDownOpen
		{
			get { return (bool) GetValue(IsDropDownOpenProperty); }
			set { SetValue(IsDropDownOpenProperty, value); }
		}

		#endregion
	}
}