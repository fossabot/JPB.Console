using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;

namespace JPB.Console.Helper.Grid.NetCore.Grid
{
	public enum ColumnGenerationMode
	{
		AutoGenerate,
		NoColumns
	}

	public class AlignedProperty
	{
		public AlignedProperty()
		{

		}

		public string Text { get; set; }
		public int Columns { get; set; }
		public int Rows { get; set; }
		public int UnusedSpace { get; set; }
	}

	public class ConsoleGrid<T>
	{
		public ConsoleGrid(ColumnGenerationMode columnGenerationMode = ColumnGenerationMode.AutoGenerate)
		{
			Target = typeof(T);
			ExpandConsole = true;
			ClearConsole = true;
			ObserveList = true;
			SelectedItems = new ObservableCollection<T>();
			SourceList = new ObservableCollection<T>();
			ConsolePropertyGridStyle = new DefaultConsolePropertyGridStyle();
			this.Columns = new List<ConsoleGridColumn>();

			_extraInfos = new StringBuilder();
			Null = "{NULL}";
			RenderTypeName = true;

			this.ColumnGenerationMode = columnGenerationMode;

			if (this.ColumnGenerationMode == ColumnGenerationMode.AutoGenerate)
			{
				GenerateColumns();
			}
		}

		public Type Target { get; set; }
		public ObservableCollection<T> SourceList { get; set; }
		public ObservableCollection<T> SelectedItems { get; set; }

		public T FocusedItem
		{
			get { return _focusedItem; }
			set
			{
				_focusedItem = value;
				if (value != null)
					this.RenderGrid();
			}
		}

		private StringBuilder _extraInfos;
		public StringBuilder ExtraInfos
		{
			get { return _extraInfos; }
			set
			{
				if (value == null)
					throw new ArgumentNullException("value");
				_extraInfos = value;
			}
		}

		private IConsolePropertyGridStyle _consolePropertyGridStyle;
		private T _focusedItem;

		public IConsolePropertyGridStyle ConsolePropertyGridStyle
		{
			get { return _consolePropertyGridStyle; }
			set
			{
				if (value == null)
					throw new ArgumentNullException("value");
				_consolePropertyGridStyle = value;
			}
		}

		/// <summary>
		/// If enabled, it will be tried to expand the console's size to its complete width
		/// If this is not possible UI Bugs will be visibile ... WIP
		/// </summary>
		public bool ExpandConsole { get; set; }

		/// <summary>
		/// Clear the console bevor drawing
		/// </summary>
		public bool ClearConsole { get; set; }

		/// <summary>
		/// Attach to the Source list and ReRender the grid when the items change
		/// </summary>
		public bool ObserveList { get; set; }

		/// <summary>
		/// The text render object for null
		/// </summary>
		public string Null { get; set; }

		/// <summary>
		/// Render a Sum text at bottom
		/// </summary>
		public bool RenderSum { get; set; }

		/// <summary>
		/// Clear the Additional Infos Builder after use
		/// </summary>
		public bool PersistendAdditionalInfos { get; set; }

		/// <summary>
		/// Add a Auto column with the Row number
		/// </summary>
		public bool RenderRowNumber { get; set; }

		/// <summary>
		/// Should the type of the Property added
		/// </summary>
		public bool RenderTypeName { get; set; }

		/// <summary>
		/// Sets the Column Generation
		/// </summary>
		public ColumnGenerationMode ColumnGenerationMode { get; private set; }

		/// <summary>
		/// All Column informations
		/// </summary>
		public List<ConsoleGridColumn> Columns { get; private set; }

		private void GenerateColumns()
		{
			if (this.Target.Namespace != null && this.Target.Namespace.Equals("System"))
			{
				Columns.Add(new ConsoleGridColumn()
				{
					AutoGenerated = true,
					GetValue = f => f,
					Name = "Value"
				});
				return;
			}


			IEnumerable<PropertyInfo> properties;

#if NET_CORE
			properties = Target.GetRuntimeProperties();
#else
			properties = Target.GetProperties();
#endif

			Columns.AddRange(properties.Select(s =>
			{
				var name = s.Name;
				if (RenderTypeName)
				{
					name = string.Format("{0} <{1}>", name, s.PropertyType.ToString());
				}

				var valueInformations = new ConsoleGridColumn()
				{
					GetValue = s.GetValue,
					Name = name,
					AutoGenerated = true
				};

				return valueInformations;
			}));
		}

		private void RecalcColumns()
		{
			foreach (var consoleGridColumn in Columns)
			{
				consoleGridColumn.MaxContentSize = SourceList.Max(e =>
				{
					var value = consoleGridColumn.GetValue(e);
					if (value != null)
					{
						return value.ToString().Length;
					}
					return Null.ToString().Length;
				});
			}
		}

		private class RenderItem
		{
			public IEnumerable<ColumnInfo> ColumnInfos { get; set; }
			public T Item { get; set; }
		}

		class ColumnInfo
		{
			public string Value { get; set; }
			public AlignedProperty Size { get; set; }
			public ConsoleGridColumn ColumnElementInfo { get; set; }
		}

		public StringBuilderInterlaced RenderGrid(bool flushToConsoleStream = true)
		{
			var stream = new StringBuilderInterlaced();

			SourceList.CollectionChanged -= SourceListOnCollectionChanged;

			if (ObserveList)
				SourceList.CollectionChanged += SourceListOnCollectionChanged;

			var size = 0;
			var fod = SourceList.FirstOrDefault();
			var length = SourceList.Count;

			if (fod == null)
			{
				return null;
			}

			RecalcColumns();

			var props = Columns;

			if (RenderRowNumber)
			{
				int fakeId = 0;
				//fake new Column
				if (!props.All(f => !f.AutoGenerated && f.Name != "Nr"))
					props.Insert(0, new ConsoleGridColumn()
					{
						AutoGenerated = true,
						Name = "Nr",
						MaxContentSize = length.ToString().Length,
						GetValue = o => fakeId++
					});
			}

			var allCollumns = props.Select(f => f.MaxContentSize).Aggregate((e, f) => e + f);

			if (allCollumns > System.Console.WindowWidth)
			{
				var partialForAllElements = System.Console.WindowWidth/props.Count - 30;
				foreach (var valueInfo in props)
				{
					if (valueInfo.MaxContentSize > partialForAllElements)
					{
						valueInfo.MaxContentSize = partialForAllElements;
						valueInfo.AlignedProperty = AlignValueToSize(valueInfo.Name, partialForAllElements);
					}
					else
					{
						valueInfo.AlignedProperty = AlignValueToSize(valueInfo.Name, valueInfo.MaxSize);
					}

					valueInfo.Name = valueInfo.AlignedProperty.Text;
					size += valueInfo.Name.Length;
				}
			}
			else
			{
				foreach (var valueInfo in props)
				{
					valueInfo.AlignedProperty = AlignValueToSize(valueInfo.Name, valueInfo.MaxSize);
					valueInfo.Name = valueInfo.AlignedProperty.Text;
					size += valueInfo.Name.Length;
				}
			}

			this.ConsolePropertyGridStyle.RenderHeader(stream, props);

			stream.AppendLine();

			if (ExpandConsole && System.Console.WindowWidth < size && System.Console.LargestWindowWidth >= size)
				System.Console.WindowWidth = size + 1;

			var toRender = new List<RenderItem>();
			foreach (var item in SourceList)
			{
				toRender.Add(new RenderItem()
				{
					Item = item,
					ColumnInfos = props.Select(f => new
					{
						Value = (f.GetValue(item) ?? Null).ToString(),
						ColumnInfo = f
					})
					.Select(f =>
					{
						var corrected = AlignValueToSize(f.Value, f.ColumnInfo.MaxSize);
						return new ColumnInfo()
						{
							Value = corrected.Text,
							Size = corrected,
							ColumnElementInfo = f.ColumnInfo
						};
					}).ToArray()
				});
			}

			var ix = 0;

			foreach (var element in toRender)
			{
				var item = element.Item;
				var rowsForThisItem = element.ColumnInfos.Max(f => f.Size.Rows);

				var selected = SelectedItems != null && SelectedItems.Contains(item);
				bool focused = FocusedItem != null && FocusedItem.Equals(item);
				for (int i = 0; i < rowsForThisItem; i++)
				{
					var propCount = 0;
					foreach (var column in element.ColumnInfos)
					{
						this.ConsolePropertyGridStyle.BeginRenderProperty(stream, propCount, length.ToString().Length, selected, focused);
						var toRenderThisRow =
							column.Value.Skip(i*column.ColumnElementInfo.MaxContentSize)
								.Take(column.ColumnElementInfo.MaxContentSize)
								.Select(f => f.ToString())
								.Aggregate((e, f) => e + f);
						this.ConsolePropertyGridStyle.RenderNextProperty(stream, toRenderThisRow, propCount, selected, focused);
						propCount++;
					}
					this.ConsolePropertyGridStyle.EndRenderProperty(stream, i, selected, focused);
					stream.AppendLine();
				}

				ix++;
			}

			//for (int i = 0; i < length; i++)
			//{
			//	var item = SourceList[i];
			//	var isNotifyable = item as INotifyPropertyChanged;
			//	if (isNotifyable != null)
			//	{
			//		isNotifyable.PropertyChanged += (sender, args) =>
			//		{
			//			RenderGrid();
			//		};
			//	}

			//	var selected = SelectedItems != null && SelectedItems.Contains(item);
			//	bool focused = FocusedItem != null && FocusedItem.Equals(item);

			//	this.ConsolePropertyGridStyle.BeginRenderProperty(stream, i, length.ToString().Length, selected, focused);

			//	for (int index = 0; index < props.Count; index++)
			//	{
			//		var propertyInfo = props[index];
			//		var value = propertyInfo.GetValue(item) ?? Null;
			//		var norm = AlignValueToSize(value.ToString(), propertyInfo.MaxSize);

			//		if (norm.Rows == 1)
			//		{
			//			this.ConsolePropertyGridStyle.RenderNextProperty(stream, norm.Text, index, selected, focused);
			//		}
			//		else
			//		{
			//			for (int j = 0; j < norm.Rows; j++)
			//			{
			//				var nextText =
			//					norm.Text.Skip(j * propertyInfo.MaxSize)
			//						.Take(propertyInfo.MaxSize)
			//						.Select(f => f.ToString())
			//						.Aggregate((e, f) => e + f);

			//				this.ConsolePropertyGridStyle.RenderNextProperty(stream, nextText, index, selected, focused);

			//				for (int k = 0; k < i; k++)
			//				{
			//					this.ConsolePropertyGridStyle.BeginRenderProperty(stream, i, length.ToString().Length, selected, focused);
			//				}
			//			}
			//		}
			//	}
			//	this.ConsolePropertyGridStyle.EndRenderProperty(stream, i, selected, focused);
			//	stream.AppendLine();
			//}


			this.ConsolePropertyGridStyle.RenderFooter(stream, props);
			stream.AppendLine();

			if (RenderSum)
			{
				this.ConsolePropertyGridStyle.RenderSummary(stream, length);
				stream.AppendLine();
			}

			if (_extraInfos.Length > 0)
			{
				this.ConsolePropertyGridStyle.RenderAdditionalInfos(stream, _extraInfos);
				stream.AppendLine();
			}

			if (!PersistendAdditionalInfos)
				_extraInfos.Clear();
			if (flushToConsoleStream)
			{
				if (ClearConsole)
					System.Console.Clear();
				stream.WriteToConsole();
			}

			return stream;
		}

		public static AlignedProperty AlignValueToSize(string source, int max)
		{
			var overflow = (int)Math.Ceiling((float)source.Length / max);

			if (overflow > 1)
			{
				max = max * overflow;
			}

			var placeLeft = max - source.Length;

			int left = placeLeft / 2;
			int right = placeLeft / 2;

			if (placeLeft > 0 && placeLeft % 2 != 0)
			{
				left += 1;
			}

			var name = "";

			for (int j = 0; j < left; j++)
			{
				name += " ";
			}

			name += source;

			for (int r = 0; r < right; r++)
			{
				name += " ";
			}

			return new AlignedProperty() { Columns = 1, Rows = (int) overflow, Text = name, UnusedSpace = name.Length - (right + left) };
		}

		private void SourceListOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs notifyCollectionChangedEventArgs)
		{
			RenderGrid();
		}

		public static void RenderList(IEnumerable<T> @select)
		{
			var grid = new ConsoleGrid<T>();
			grid.SourceList = new ObservableCollection<T>(@select);
			grid.RenderGrid();
		}
	}
}