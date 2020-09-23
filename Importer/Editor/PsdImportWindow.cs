/*
MIT License

Copyright (c) 2017 Jeiel Aranal

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using SubjectNerd.PsdImporter.FullSerializer;
using SubjectNerd.PsdImporter.Reconstructor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SubjectNerd.PsdImporter
{
	public class PsdImportWindow : EditorWindow
	{
		private const string PrefKeyAdvancedMode = "SubjectNerdAgreement.Psd.Advanced";

		#region Static
		private const string MENU_ASSET_IMPORT = "Assets/Import PSD Layers";

		private static PsdImportWindow OpenWindow()
		{
			var win = GetWindow<PsdImportWindow>();
			win.titleContent = new GUIContent("PSD Importer");
			win.Show(true);
			win.minSize = new Vector2(550, 550);
			win.autoRepaintOnSceneChange = true;
			return win;
		}

		[MenuItem(MENU_ASSET_IMPORT)]
		private static void ImportPsdAsset()
		{
			var win = OpenWindow();

			Object[] selectionArray = Selection.objects;
			if (selectionArray.Length < 1)
				return;

			for (int i = 0; i < selectionArray.Length; i++)
			{
				var file = selectionArray[i];
				var path = AssetDatabase.GetAssetPath(file);
				if (path.ToLower().EndsWith(".psd"))
				{
					win.OpenFile(file);
					return;
				}
			}
		}

		public static bool OpenLayerImporter(Object file)
		{
			var win = OpenWindow();
			if (file == null)
				return false;

			var path = AssetDatabase.GetAssetPath(file);
			if (string.IsNullOrEmpty(path))
				return false;
			if (path.ToLower().EndsWith(".psd") == false)
				return false;

			win.OpenFile(file);
			return true;
		}
		#endregion

		private bool isOpeningFile;
		private float importPPU = 100;
		private Object importFile;
		private string importPath;
		private ImportUserData importSettings;
		private DisplayLayerData importDisplay;
		private Texture2D importPreview;
		private bool settingsChanged;

		private readonly List<int[]> importLayersList = new List<int[]>();
		private readonly List<int[]> quickSelect = new List<int[]>();
		private int selectionCount = 0;

		fsSerializer serializer;
		private Type typeImportUserData;
		private bool showImportSettings = true;
		private int[] lastSelectedLayer;

		private readonly Dictionary<int[], Rect> layerRectLookup = new Dictionary<int[], Rect>();
		private bool isChangingSelection;
		private bool selectionChangeState;
		private string searchFilter = "";
		private bool showHeader = true;
		private bool isAdvancedMode = false;

		private IReconstructor[] reconstructors;

		//Custom
		private Dictionary<ImportLayerData, ImportLayerData> groupCenterLayer = new Dictionary<ImportLayerData, ImportLayerData>();

		#region UI fields

		private readonly GUIContent labelHeader = new GUIContent("Import PSD Layers");
		private readonly GUIContent labelAdvanced = new GUIContent("Advanced");
		private readonly GUIContent labelFileNaming = new GUIContent("File Names");
		private readonly GUIContent labelGrpMode = new GUIContent("Group Mode");
		private readonly GUIContent labelPackTag = new GUIContent("Packing Tag");
		private readonly GUIContent labelPixelUnit = new GUIContent("Pixels Per Unit");
		private readonly GUIContent labelAlignment = new GUIContent("Default Pivot");
		private readonly GUIContent labelFile = new GUIContent("Import PSD");
		private readonly GUIContent labelShowImport = new GUIContent("Import Settings");
		private readonly GUIContent labelScale = new GUIContent("Default Scale");
		private readonly GUIContent labelPath = new GUIContent("Import Folder");
		private readonly GUIContent labelPickPath = new GUIContent("Open");
		//private readonly GUIContent labelAutoImport = new GUIContent("Auto Import");
		private readonly GUIContent labelUseConstructor = new GUIContent("Reconstructor");
		private readonly GUIContent labelSelConstructor = new GUIContent("Construct As");
		private readonly GUIContent labelDocAlign = new GUIContent("Document Alignment");
		private readonly GUIContent labelDocPivot = new GUIContent("Document Pivot");
		private readonly GUIContent labelLoadBoxEmpty = new GUIContent("No PSD File Loaded");

		private int selectedReconstructor;
		private GUIContent[] dropdownReconstruct;

		private bool stylesLoaded = false;
		private GUIStyle styleHeader, styleBoldFoldout,
						styleLayerEntry, styleLayerSelected,
						styleVisOn, styleVisOff, styleLoader,
						styleToolbar, styleToolSearch, styleToolCancel,
						styleLoadBoxEmpty;
		private Texture2D icnFolder, icnTexture;
		private GUILayoutOption layerHeight;
		private GUILayoutOption noExpandW;
		private GUILayoutOption bigButton;

		// Column widths
		private Rect rImportToggle, rVisible, rLayerDisplay, rPivot, rScaling, rMakeDefault;
		private Vector2 rTableSize;
		private bool tableWillScroll;
		private float layerEntryYMax;

		private Vector2 scrollPos;
		private int indentLevel = 0;
		private const float indentWidth = 20;

		private void LoadStyles()
		{
			if (stylesLoaded)
				return;
			
			layerHeight = GUILayout.Height(EditorGUIUtility.singleLineHeight + 5);
			noExpandW = GUILayout.ExpandWidth(false);
			bigButton = GUILayout.Height(30);
			icnFolder = EditorGUIUtility.FindTexture("Folder Icon");
            icnTexture = EditorGUIUtility.FindTexture("DefaultAsset Icon");


            styleHeader = new GUIStyle(GUI.skin.label)
			{
				alignment = TextAnchor.MiddleCenter,
				fontStyle = FontStyle.Bold
			};

			var tempStyle = GUI.skin.FindStyle("EyeDropperHorizontalLine");
			
			styleLayerEntry = new GUIStyle(GUI.skin.box)
			{
				margin = new RectOffset(0, 0, 0, 0),
				padding = new RectOffset(0, 0, 0, 0),
				contentOffset = new Vector2(0, 0),
				normal = new GUIStyleState() { background = tempStyle.normal.background }
			};

			tempStyle = GUI.skin.FindStyle("HelpBox");

			styleLayerSelected = new GUIStyle(GUI.skin.box)
			{
				margin = new RectOffset(0, 0, 0, 0),
				padding = new RectOffset(0, 0, 0, 0),
				contentOffset = new Vector2(0, 0),
                normal = new GUIStyleState() { background = tempStyle.normal.background }
			};

			styleLoader = new GUIStyle(GUI.skin.FindStyle("ObjectFieldThumb"))
			{
				//margin = new RectOffset(0, 0, 15, 0),
				padding = new RectOffset(0, 0, 5, 0),
				alignment = TextAnchor.LowerCenter,
				//fixedHeight = 200,
				//fixedWidth = 200,
				stretchWidth = true
			};

			styleLoadBoxEmpty = new GUIStyle()
			{
				//Preview 넓이,높이를 기준으로 계산
				padding = new RectOffset(0, 100, 5, 100),
				alignment = TextAnchor.MiddleCenter,
				wordWrap = true
			};

			styleBoldFoldout = new GUIStyle(EditorStyles.foldout)
			{
				fontStyle = FontStyle.Bold
			};

			tempStyle = new GUIStyle() { normal = new GUIStyleState() { background = EditorGUIUtility.Load("Icons/animationvisibilitytoggleoff.png") as Texture2D }
                , onNormal = new GUIStyleState() { background = EditorGUIUtility.Load("Icons/animationvisibilitytoggleon.png") as Texture2D }
                , fixedHeight = 11, fixedWidth = 13
                , border = new RectOffset(2, 2, 2, 2), overflow = new RectOffset(-1, 1, -2, 2)
                , padding = new RectOffset(3, 3, 3, 3)
                , richText = false, stretchHeight = false
                , stretchWidth = false
            };

            styleVisOff = new GUIStyle(tempStyle)
			{
				margin = new RectOffset(10, 10, 3, 3)
			};
			styleVisOn = new GUIStyle(tempStyle)
			{
				normal = new GUIStyleState() { background = tempStyle.onNormal.background },
				margin = new RectOffset(10, 10, 3, 3)
			};

			styleToolbar = GUI.skin.FindStyle("Toolbar");
			styleToolSearch = GUI.skin.FindStyle("ToolbarSeachTextField");
			styleToolCancel = GUI.skin.FindStyle("ToolbarSeachCancelButton");

			stylesLoaded = true;
		}
		#endregion

		private void OnEnable()
		{
			Selection.selectionChanged -= HandleSelectionChange;
			Selection.selectionChanged += HandleSelectionChange;

			stylesLoaded = false;
			isAdvancedMode = EditorPrefs.GetBool(PrefKeyAdvancedMode, false);
			typeImportUserData = typeof(ImportUserData);
			serializer = new fsSerializer();

			// Find implementations of IReconstructor
			var type = typeof(IReconstructor);
			var types = AppDomain.CurrentDomain.GetAssemblies()
								.SelectMany(s => s.GetTypes())
								.Where(p => p != type && type.IsAssignableFrom(p));

			List<IReconstructor> constructor_list = new List<IReconstructor>();
			List<GUIContent> constructor_dropdown = new List<GUIContent>();

			foreach (Type type_constructor in types)
			{
				var instance = Activator.CreateInstance(type_constructor);
				var r_instance = (IReconstructor)instance;
				if (r_instance != null)
				{
					constructor_list.Add(r_instance);
					constructor_dropdown.Add(new GUIContent(r_instance.DisplayName));
				}
			}

			reconstructors = constructor_list.ToArray();
			dropdownReconstruct = constructor_dropdown.ToArray();
			importSettings = new ImportUserData();
		}

		private void OnDestroy()
		{
			Selection.selectionChanged -= HandleSelectionChange;
			reconstructors = null;
			dropdownReconstruct = null;
		}

		private void HandleSelectionChange()
		{
			Repaint();
		}

		public void OpenFile(Object fileObject)
		{
			if (isOpeningFile)
				return;

			importFile = null;
			importPath = string.Empty;
			importPreview = null;
			importPPU = 100;

			importSettings = new ImportUserData() {DocAlignment = SpriteAlignment.Center};
			importDisplay = null;

			selectionCount = 0;
			quickSelect.Clear();
			lastSelectedLayer = null;

			var filePath = AssetDatabase.GetAssetPath(fileObject);
			if (filePath.ToLower().EndsWith(".psd") == false)
			{
				return;
			}

			importFile = fileObject;
			importPath = filePath;
			importPreview = AssetDatabase.LoadAssetAtPath<Texture2D>(importPath);

			// Read the texture import settings of the asset file
			TextureImporter textureImporter = (TextureImporter)AssetImporter.GetAtPath(importPath);
			TextureImporterSettings unityImportSettings = new TextureImporterSettings();
			textureImporter.ReadTextureSettings(unityImportSettings);

			importPPU = unityImportSettings.spritePixelsPerUnit;

			// Attempt to deserialize
			string json = textureImporter.userData;
			bool didGetUserData = false;
			if (string.IsNullOrEmpty(json) == false)
			{
				fsData data = fsJsonParser.Parse(json);
				object deserialObj = null;
				if (serializer.TryDeserialize(data, typeImportUserData, ref deserialObj)
							.AssertSuccessWithoutWarnings()
							.Succeeded)
				{
					importSettings = (ImportUserData)deserialObj;
					if (importSettings == null)
						importSettings = new ImportUserData();
					else
						didGetUserData = true;
				}
			}

			if (didGetUserData)
			{
				settingsChanged = false;
			}
			else
			{
				settingsChanged = true;
				showImportSettings = true;
			}

			isOpeningFile = true;
			PsdImporter.BuildImportLayerData(importFile, importSettings, (layerData, displayData) =>
			{
				importSettings.DocRoot = ResolveData(importSettings.DocRoot, layerData);
				importDisplay = displayData;
				isOpeningFile = false;
				CollateImportList();

				groupCenterLayer.Clear();

				layerData.Iterate(
					layerCallback: layer =>
					{
						DisplayLayerData displayLayerData = GetDisplayData(layer.indexId);
						if (displayLayerData.isGroup && !groupCenterLayer.ContainsKey(layer))
							groupCenterLayer.Add(layer, null);

						InitializeSortingLayerOfGroup(layer);
					}
				);

				Repaint();
			});
		}

		/// <summary>
		/// Resolve setting differences from stored data and built data
		/// </summary>
		/// <param name="storedData"></param>
		/// <param name="builtData"></param>
		/// <returns></returns>
		private ImportLayerData ResolveData(ImportLayerData storedData, ImportLayerData builtData)
		{
			// Nothing was stored, used built data
			if (storedData == null)
				return builtData;

			// Flatten out stored to a dictionary, using the path as keys
			Dictionary<string, ImportLayerData> storedIndex = new Dictionary<string, ImportLayerData>();
			storedData.Iterate(
				layerCallback: layer =>
				{
					if (storedIndex.ContainsKey(layer.path) == false)
						storedIndex.Add(layer.path, layer);
					else
						storedIndex[layer.path] = layer;
				}
			);

			// Iterate through the built data now, checking for settings from storedIndex
			builtData.Iterate(
				layerCallback: layer =>
				{
					ImportLayerData existingSettings;
					if (storedIndex.TryGetValue(layer.path, out existingSettings))
					{
						layer.useDefaults = existingSettings.useDefaults;
						layer.Alignment = existingSettings.Alignment;
						layer.Pivot = existingSettings.Pivot;
						layer.ScaleFactor = existingSettings.ScaleFactor;
						layer.import = existingSettings.import;
					}
				}
			);


			return builtData;
		}

		private void OnGUI()
		{
			LoadStyles();
			
			switch (Event.current.commandName)
			{
				case "ObjectSelectorUpdated":
				case "ObjectSelectorClosed":
					OpenFile(EditorGUIUtility.GetObjectPickerObject());
					Repaint();
					break;
			}
		
            using (new GUILayout.VerticalScope())
            {
                using (new GUILayout.VerticalScope())
                {
                    DrawLayerTableToolbar();

                    using (new GUILayout.HorizontalScope())
                    {
                        using (new GUILayout.VerticalScope(("box"), GUILayout.MaxWidth(350)))
                        {
                            using (new GUILayout.VerticalScope( GUILayout.MinHeight(350)))
                            {
                                DrawPsdOperations();

                                DrawImportSettings();
                            }

                            EditorGUILayout.Space(10.0f);

                            DrawReconstructor();
                        }
                        
                        DrawLayerList();
                    }
                }
            }
        }

		private float CalculateColumns()
		{
			// Calculate the sizes for the columns
			var width = EditorGUIUtility.currentViewWidth;
			var height = EditorGUIUtility.singleLineHeight + 10;
			int padTop = 4;
			int padBetween = 3;
			rImportToggle = new Rect(10, padTop, 16, height);
			rVisible = new Rect(rImportToggle.xMax + padBetween, padTop + 2, 10, height);
			rMakeDefault = new Rect(0, padTop, 20, EditorGUIUtility.singleLineHeight);

			var distWidth = width - rMakeDefault.width - rVisible.xMax;
			distWidth -= tableWillScroll ? 40 : 25;

			var pivotWidth = Mathf.Clamp(distWidth * 0.2f, 95, 120);
			var scaleWidth = Mathf.Clamp(distWidth * 0.2f, 65, 120);
			var layerWidth = Mathf.Max(distWidth - pivotWidth - scaleWidth, 150);
			rLayerDisplay = new Rect(rVisible.xMax + 10, 2, layerWidth, height - 4);
			rPivot = new Rect(rLayerDisplay.xMax + padBetween, padTop, pivotWidth, height);
			rScaling = new Rect(rPivot.xMax + padBetween, padTop, scaleWidth, height);
			rMakeDefault.x = rScaling.xMax + padBetween;

			rTableSize = new Vector2(rMakeDefault.xMax, height);
			if (isAdvancedMode == false)
			{
				rTableSize.x = width;
				if (tableWillScroll)
					rTableSize.x -= 20;
			}
			return width;
		}

		private void DrawLayerTableToolbar()
		{
			using (new GUILayout.HorizontalScope(styleToolbar))
			{
				searchFilter = EditorGUILayout.TextField(searchFilter, styleToolSearch, GUILayout.ExpandWidth(true));
				if (GUILayout.Button(GUIContent.none, styleToolCancel))
				{
					searchFilter = string.Empty;
					GUI.FocusControl(null);
				}

				if (showHeader)
				{
					string fileName = importFile ? "File Name : " + importFile.name : "Empty PSD File";
					EditorGUILayout.LabelField(new GUIContent(fileName), styleHeader, noExpandW);
				}

				//AdvancedMode 개선해야 할 기능 중 하나이다
				using (var check = new EditorGUI.ChangeCheckScope())
				{
					isAdvancedMode = GUILayout.Toggle(isAdvancedMode, labelAdvanced, EditorStyles.miniButton, noExpandW);
					if (check.changed)
						EditorPrefs.SetBool(PrefKeyAdvancedMode, isAdvancedMode);
				}
				
				if (Event.current.type == EventType.Repaint)
				{
					showHeader = string.IsNullOrEmpty(searchFilter);
					if (showHeader == false)
						showHeader = EditorGUIUtility.currentViewWidth > 400;
				}
			}
		}

		private void DrawLayerList()
		{
			using (new GUILayout.VerticalScope())
			{
				var width = CalculateColumns();

				using(new GUILayout.HorizontalScope("box"))
				{
					GUILayout.Label("Layers");

/*                    if (isAdvancedMode)
                    {
                        GUILayout.Label("Pivot");
                        GUILayout.Label("Scale");
                        *//*GUI.Label(new Rect(rPivot) { y = rHeader.y, x = rPivot.x - scrollPos.x }, "Pivot");
                        GUI.Label(new Rect(rScaling) { y = rHeader.y, x = rScaling.x - scrollPos.x }, "Scale");*//*
                    }*/
                }

                using (var scrollView = new GUILayout.ScrollViewScope(scrollPos, EditorStyles.helpBox))
				{
					scrollPos = scrollView.scrollPosition;

					DrawLayerContents();
				}

				if (Event.current.type == EventType.Repaint)
				{
					var scrollArea = GUILayoutUtility.GetLastRect();
					var newWillScroll = layerEntryYMax > scrollArea.yMax;
					if (newWillScroll != tableWillScroll)
					{
						tableWillScroll = newWillScroll;
						Repaint();
					}
				}

                //Buttons
                using (new EditorGUI.DisabledGroupScope(importFile == null))
                {
                    var btnW = GUILayout.MaxWidth(EditorGUIUtility.currentViewWidth / 2f);
                    using (new GUILayout.HorizontalScope())
                    {
   /*                     if (GUILayout.Button("Save Selection", btnW))
                        {
                            AddQuickSelect();
                            Repaint();
                            WriteImportSettings();
                        }

                        if (GUILayout.Button("Clear Selection", btnW))
                        {
                            quickSelect.Clear();
                            selectionCount = 0;
                            lastSelectedLayer = null;
                        }*/
                    }

                    using (new GUILayout.HorizontalScope())
                    {
                        string textImport = string.Format("Import All Layers ({0})", importLayersList.Count);
                        //string textQuickImport = string.Format("Import Selected ({0})", selectionCount);
                        //if (GUILayout.Button(textImport, bigButton, btnW))
                        if (GUILayout.Button(textImport, bigButton))
                            PsdImporter.ImportLayersUI(importFile, importSettings, importLayersList);

                  /*      if (GUILayout.Button(textQuickImport, bigButton, btnW))
                            PsdImporter.ImportLayersUI(importFile, importSettings, quickSelect);*/
                    }
                }

                GUILayout.Box(GUIContent.none, GUILayout.Height(4f), GUILayout.ExpandWidth(true));
			}
		}

		private void DrawLayerContents()
		{
			if (importFile == null)
			{
				EditorGUILayout.HelpBox("No PSD file loaded", MessageType.Error, true);
				return;
			}

			if (importSettings == null || importSettings.DocRoot == null || importDisplay == null)
			{
				if (importFile != null)
					OpenFile(importFile);
				return;
			}

			DrawIterateLayers(importSettings.DocRoot);
		}

		private void DrawIterateLayers(ImportLayerData layerData)
		{
			layerRectLookup.Clear();
			layerEntryYMax = 0;
			
			layerData.Iterate(
				layerCallback: layer =>
				{
					var display = GetDisplayData(layer.indexId);
					if (display == null)
						return;

					if (string.IsNullOrEmpty(searchFilter) == false)
					{
						if (layer.name.ToLower().Contains(searchFilter.ToLower()) == false)
							return;
					}

					DrawLayerEntry(layer, display);
				},
				canEnterGroup: layer =>
				{
					var display = GetDisplayData(layer.indexId);
					if (display == null)
						return true;
					if (string.IsNullOrEmpty(searchFilter) == false)
						return true;
					return display.isOpen;
				},
				enterGroupCallback: data => indentLevel++,
				exitGroupCallback: data => indentLevel--
			);

			// Process mouse events

			var evt = Event.current;
			bool willChangeSelection = false;
			int[] targetLayer = null;
			foreach (var kvp in layerRectLookup)
			{
				if (kvp.Value.Contains(evt.mousePosition))
				{
					var checkLayer = GetLayerData(kvp.Key);
					if (checkLayer == null || checkLayer.Childs == null)
						continue;

					bool layerIsGroup = checkLayer.Childs.Count > 0;

					if (evt.type == EventType.MouseDown)
					{
						willChangeSelection = layerIsGroup == false;
						bool willAddSelect = quickSelect.Contains(kvp.Key) == false;
						if (willAddSelect)
							lastSelectedLayer = kvp.Key;

						if (willChangeSelection)
						{
							isChangingSelection = true;
							targetLayer = kvp.Key;
							selectionChangeState = willAddSelect;
						}
						else
						{
							RecursiveQuickSelect(checkLayer, willAddSelect);
							if (willAddSelect == false)
								lastSelectedLayer = null;
							Repaint();
						}
						evt.Use();
					}

					if (evt.type == EventType.MouseUp)
					{
						isChangingSelection = false;
					}

					if (isChangingSelection && evt.type == EventType.MouseDrag && layerIsGroup == false)
					{
						willChangeSelection = true;
						targetLayer = kvp.Key;
					}
					break;
				}
			}

			if (willChangeSelection && targetLayer != null)
			{
				SetQuickSelect(targetLayer, selectionChangeState);
				GetSelectCount();
				Repaint();
			}
		}

		private void DrawLayerEntry(ImportLayerData layer, DisplayLayerData display)
		{
			bool isGroup = display.isGroup;
			bool isSelected = quickSelect.Contains(layer.indexId);
			GUIStyle entryStyle = isSelected ? styleLayerSelected : styleLayerEntry;

			using (new GUILayout.HorizontalScope(entryStyle, layerHeight))
			{
                bool parentWillImport = ParentWillImport(layer.indexId);
     
                //Disable Checkbox Control
                using (new EditorGUI.DisabledScope(parentWillImport == false))
				{
					var displayImport = layer.import && parentWillImport;

                    using (var changed = new EditorGUI.ChangeCheckScope())
					{
						displayImport = GUILayout.Toggle(displayImport, GUIContent.none, GUILayout.MaxWidth(15));

						if (changed.changed && parentWillImport)
						{
							layer.import = displayImport;
							CollateImportList();
							quickSelect.Clear();
							selectionCount = 0;
							lastSelectedLayer = null;
						}
					}
				}

				//Disable checkbox에 따른 눈깔 아이콘
				using (new EditorGUI.DisabledScope(true))
				{
					var visStyle = display.isVisible ? styleVisOn : styleVisOff;
					GUILayout.Label(GUIContent.none, visStyle);
				}
				
				//rLayer.xMin += indentLevel*indentWidth;

				GUIContent layerContent = new GUIContent()
				{
					image = isGroup ? icnFolder : icnTexture,
					text = layer.name
				};
				
				//그룹 리스트 아이템이면
				if (isGroup)
				{
					float min, max;
					EditorStyles.popup.CalcMinMaxWidth(layerContent, out min, out max);
                    //rLayer.width = min;
                    display.isOpen = EditorGUILayout.Foldout(display.isOpen, layerContent, new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold }); ;
                    //display.isOpen = EditorGUI.Foldout(rLayer, display.isOpen, layerContent);

                    DrawGroupSortingLayer(layer);

					DrawGroupZDistanceField(layer);

				}
                //자식 레이어 리스트 아이템이면
                else
				{
                    //EditorGUI.LabelField(rLayer, layerContent);
                    EditorGUILayout.LabelField(layerContent, new GUIStyle()
                    {
                        margin = new RectOffset(0, 0, 0, 0),
                        padding = new RectOffset(40, 0, 0, 0),
                        contentOffset = new Vector2(0, 0),
                        //normal = new GUIStyleState() { background = tempStyle.normal.background }
                    });

					DrawLayerCenterSlector(layer);

                    if (isAdvancedMode)
					{
						//앵글 옮겨야함
						DrawLayerAdvanced(layer);
					}
				}
			}

			Rect layerRect = GUILayoutUtility.GetLastRect();
			layerRect.xMin += 40;
			layerRectLookup.Add(layer.indexId, layerRect);
			layerEntryYMax = Mathf.Max(layerEntryYMax, layerRect.yMax);
		}


        private void DrawGroupSortingLayer(ImportLayerData layer)
        {
            using (var check = new EditorGUI.ChangeCheckScope() )
            {
                List<string> tagList = new List<string>();

                foreach (var iter in SortingLayer.layers)
                    tagList.Add(iter.name.ToString());

                layer.sortingLayer = EditorGUILayout.Popup(GUIContent.none, layer.sortingLayer, tagList.ToArray(), GUILayout.MaxWidth(100));
            }
        }

		private void DrawGroupZDistanceField(ImportLayerData layer)
		{
			using (var check = new EditorGUI.ChangeCheckScope())
			{
				layer.zDistance = EditorGUILayout.FloatField(layer.zDistance, GUILayout.MaxWidth(50));
			}
		}
       

		private void DrawLayerCenterSlector(ImportLayerData layer)
		{
			using(new GUILayout.HorizontalScope())
			{
				using (var check = new EditorGUI.ChangeCheckScope())
				{
					layer.isCenter = GUILayout.Toggle(layer.isCenter, GUIContent.none, GUILayout.MaxWidth(50));

					if (check.changed)
					{
						ImportLayerData parent = FindGroupLayerByChildLayer(layer);

						SetCenterLayer(parent, layer);
					}
				}

				if(layer.isCenter)
					layer.layerInterval = EditorGUILayout.FloatField(layer.layerInterval, GUILayout.MaxWidth(50));
			}
		}

		private ImportLayerData FindGroupLayerByChildLayer(ImportLayerData layerData)
		{
			foreach(var iter in groupCenterLayer)
			{
				foreach(var layer in iter.Key.Childs)
				{
					if (layer == layerData)
						return iter.Key;
				}
			}

			return null;
		}

		private void SetCenterLayer(ImportLayerData group, ImportLayerData child)
		{
			if(groupCenterLayer.ContainsKey(group))
			{
				float interval = 0.0f;

				if(groupCenterLayer[group] != null)
				{
					groupCenterLayer[group].isCenter = false;

					interval = groupCenterLayer[group].layerInterval;
				}

				if (groupCenterLayer[group] == child)
				{
					groupCenterLayer[group] = null;
					return;
				}

				child.isCenter = true;
				child.layerInterval = interval;

				groupCenterLayer[group] = child;
			}
		}

        private void DrawLayerAdvanced(ImportLayerData layer)
		{
			using (var check = new EditorGUI.ChangeCheckScope())
			{
				layer.Alignment = (SpriteAlignment)EditorGUILayout.EnumPopup(GUIContent.none, layer.Alignment , GUILayout.MaxWidth(100));
				layer.ScaleFactor = (ScaleFactor)EditorGUILayout.EnumPopup( GUIContent.none, layer.ScaleFactor, GUILayout.MaxWidth(100));

				if (check.changed)
				{
					layer.useDefaults = false;
					settingsChanged = true;
				}
			}

/*			using (new EditorGUI.DisabledGroupScope(layer.useDefaults))
			{
				if (GUILayout.Button("R", EditorStyles.miniButton))
				{
					settingsChanged = true;
					layer.useDefaults = true;
					layer.Pivot = importSettings.DefaultPivot;
					layer.Alignment = importSettings.DefaultAlignment;
					layer.ScaleFactor = importSettings.ScaleFactor;
				}
			}*/
		}

		private void DrawPsdOperations()
		{
			using(new GUILayout.VerticalScope())
			{
				bool noFile = importFile == null;
				GUIContent fileLabel = noFile ? labelFile : new GUIContent(importFile.name);

				GUIStyle fileTitleStyle = EditorStyles.boldLabel;
				fileTitleStyle.alignment = TextAnchor.LowerRight;

				GUILayout.Label(importPreview, styleLoader, GUILayout.MaxHeight(200), GUILayout.MinWidth(350), GUILayout.MaxWidth(350));
				Rect rLabel = GUILayoutUtility.GetLastRect();

				if (noFile)
				{
					GUI.Label(rLabel, labelLoadBoxEmpty, styleLoadBoxEmpty);
				}

				Rect rPing = new Rect(rLabel) { yMax = rLabel.yMax - 15 };
				Rect rSelect = new Rect(rLabel)
				{
					yMin = rLabel.yMax - 20,
					xMin = rLabel.xMax - 50
				};

				if (GUI.Button(rPing, GUIContent.none, new GUIStyle()))
				{
					EditorGUIUtility.PingObject(importFile);
				}

				if (GUI.Button(rSelect, "Select", EditorStyles.toolbarButton))
				{
					int controlID = GUIUtility.GetControlID(FocusType.Passive);
					EditorGUIUtility.ShowObjectPicker<Texture2D>(importFile, false, string.Empty, controlID);
				}

				var evt = Event.current;
				bool isDrag = evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform;
				if (isDrag && rLabel.Contains(evt.mousePosition))
				{
					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
					if (evt.type == EventType.DragPerform)
					{
						DragAndDrop.AcceptDrag();
						foreach (Object obj in DragAndDrop.objectReferences)
						{
							Texture2D objTexture = obj as Texture2D;
							if (objTexture == null)
								continue;
							string path = AssetDatabase.GetAssetPath(obj);
							if (path.ToLower().EndsWith(".psd") == false)
								continue;
							OpenFile(obj);
						}
					}
				}
			}
		}

		private Vector2 recontructorScroll;

		private void DrawReconstructor()
		{
			if (importSettings == null)
				return;

			EditorGUILayout.LabelField(labelUseConstructor, EditorStyles.foldoutHeader);

			using (var scroll = new GUILayout.ScrollViewScope(recontructorScroll))
			{
				recontructorScroll = scroll.scrollPosition;

				ImportLayerData reconstructLayer = null;
				if (lastSelectedLayer != null)
				{
					reconstructLayer = GetLayerData(lastSelectedLayer);
					if (reconstructLayer != null && reconstructLayer.Childs.Count == 0)
					{
						reconstructLayer = null;
					}
				}

				selectedReconstructor = EditorGUILayout.Popup(labelSelConstructor,
															selectedReconstructor,
															dropdownReconstruct);

/*				using (new GUILayout.HorizontalScope())
				{
					EditorGUILayout.PrefixLabel(labelAlignment);

					SpriteAlignUI.DrawGUILayout(importSettings.DocAlignment,
						alignment =>
						{
							importSettings.DocAlignment = alignment;
							if (alignment != SpriteAlignment.Custom)
								importSettings.DocPivot = PsdImporter.AlignmentToPivot(alignment);
							Repaint();
						});
				}

				if (importSettings.DocAlignment == SpriteAlignment.Custom)
				{
					EditorGUI.indentLevel++;
					importSettings.DocPivot = EditorGUILayout.Vector2Field(labelDocPivot, importSettings.DocPivot);
					EditorGUI.indentLevel--;
				}*/

				bool canReconstruct = reconstructLayer != null;
				IReconstructor reconstructorInstance = null;
				if (canReconstruct)
				{
					if (selectedReconstructor > -1 && selectedReconstructor < reconstructors.Length)
					{
						reconstructorInstance = reconstructors[selectedReconstructor];
						//Selected GameObject
						canReconstruct = reconstructorInstance.CanReconstruct(Selection.activeGameObject);
					}
					else
					{
						canReconstruct = false;
					}
				}

				using (new GUILayout.VerticalScope())
				{
					//Create GameObject To Hierarchy
					if (canReconstruct)
					{
						string strButton = string.Format("Build {0} as {1}", reconstructLayer.name, reconstructorInstance.DisplayName);
						if (GUILayout.Button(strButton, bigButton))
						{
							GetLayerData(lastSelectedLayer);
							PsdImporter.Reconstruct(importFile, importSettings, reconstructLayer,
													importSettings.DocPivot, reconstructorInstance);
						}
					}
					else
					{
						string helpMessage = "Select a layer group";
						if (reconstructLayer != null && reconstructorInstance != null)
							helpMessage = reconstructorInstance.HelpMessage;
						EditorGUILayout.HelpBox(helpMessage, MessageType.Info);
					}

					EditorGUILayout.Space(10.0f);

					if (GUILayout.Button("Build All Layers", bigButton))
					{
						importSettings.DocRoot.Iterate(
							layerCallback: layer =>
							{
								DisplayLayerData displayLayerData = GetDisplayData(layer.indexId);
								
								if (displayLayerData.isGroup)
									PsdImporter.Reconstruct(importFile, importSettings, layer,
															importSettings.DocPivot, reconstructors[0]);
							},
							null,
							enterGroupCallback: data => indentLevel++,
							exitGroupCallback: data => indentLevel--
						);

						foreach (int[] iter in importLayersList)
						{
							ImportLayerData layerData = importSettings.GetLayerData(iter);
							//layerData
						/*	PsdImporter.Reconstruct(importFile, importSettings, layerData,
													importSettings.DocPivot, reconstructorInstance);*/
							/*if (importSettings.GetLayerData(iter).Childs.Count > 0)
								PsdImporter.Reconstruct(importFile, importSettings, importSettings.GetLayerData(iter),
													importSettings.DocPivot, reconstructorInstance);*/
						}
						//importSettings.GetLayerData(layerIdx);
/*						foreach (var iter in importSettings.DocRoot.Childs)
						{
							if(iter.Childs.Count > 0)
								PsdImporter.Reconstruct(importFile, importSettings, iter,
													importSettings.DocPivot, reconstructorInstance);
						}*/
					}
				}
			}
		}

		private void DrawImportSettings()
		{
			using (var check = new EditorGUI.ChangeCheckScope())
			{
				showImportSettings = EditorGUILayout.Foldout(showImportSettings, labelShowImport, styleBoldFoldout);
/*				if (check.changed)
				{
					Rect window = position;
					int expand = 165;
					window.yMax += showImportSettings ? expand : -expand;
					position = window;
				}*/
			}

			if (showImportSettings == false)
				return;
			
			EditorGUI.indentLevel++;
            using (new EditorGUI.DisabledGroupScope(importFile == null))
            {
                EditorGUILayout.Space();

                EditorGUI.BeginChangeCheck();
                using(var change = new EditorGUI.ChangeCheckScope())
                {
                    DrawPathPicker();

                    importSettings.fileNaming = (NamingConvention)EditorGUILayout.EnumPopup(labelFileNaming, importSettings.fileNaming);

                    if (importSettings.fileNaming != NamingConvention.LayerNameOnly)
                    {
                        EditorGUI.indentLevel++;
                        importSettings.groupMode = (GroupMode)EditorGUILayout.EnumPopup(labelGrpMode, importSettings.groupMode);
                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.Space();

                    importSettings.PackingTag = EditorGUILayout.TextField(labelPackTag, importSettings.PackingTag);
                    importPPU = EditorGUILayout.FloatField(labelPixelUnit, importPPU);

                    using(new GUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PrefixLabel(labelAlignment);

                        SpriteAlignUI.DrawGUILayout(importSettings.DefaultAlignment, newAlign =>
                        {
                            importSettings.DefaultAlignment = newAlign;
                            settingsChanged = true;
                            ApplyDefaultSettings();
                        });
                    }


                    if (importSettings.DefaultAlignment == SpriteAlignment.Custom)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(EditorGUIUtility.labelWidth);
                            importSettings.DefaultPivot = EditorGUILayout.Vector2Field(GUIContent.none, importSettings.DefaultPivot);
                        }
                    }

                    importSettings.ScaleFactor = (ScaleFactor)EditorGUILayout.EnumPopup(labelScale, importSettings.ScaleFactor);
                    //importSettings.AutoImport = EditorGUILayout.Toggle(labelAutoImport, importSettings.AutoImport);

                    if (change.changed)
                    {
                        settingsChanged = true;
                        importSettings.DefaultPivot.x = Mathf.Clamp01(importSettings.DefaultPivot.x);
                        importSettings.DefaultPivot.y = Mathf.Clamp01(importSettings.DefaultPivot.y);
                        ApplyDefaultSettings();
                    }
                }

                

                using (new EditorGUI.DisabledGroupScope(settingsChanged == false))
                {
                    if (GUILayout.Button("Apply"))
                        WriteImportSettings();
                }
            }

			EditorGUI.indentLevel--;
		}

		private void QuickSelectApplySettings(SpriteAlignment alignment, Vector2 pivot, ScaleFactor scale, bool defaults)
		{
			if (defaults)
			{
				alignment = importSettings.DefaultAlignment;
				pivot = importSettings.DefaultPivot;
				scale = importSettings.ScaleFactor;
			}
			foreach (int[] layerIdx in quickSelect)
			{
				var layer = GetLayerData(layerIdx);
				if (layer == null || layer.Childs.Count > 0)
					continue;
				layer.useDefaults = defaults;
				layer.Alignment = alignment;
				layer.Pivot = pivot;
				layer.ScaleFactor = scale;
			}
		}

		private void ApplyDefaultSettings()
		{
			importSettings.DocRoot.Iterate(
				layerCallback: layer =>
				{
					if (layer.useDefaults)
					{
						layer.Alignment = importSettings.DefaultAlignment;
						layer.Pivot = importSettings.DefaultPivot;
						layer.ScaleFactor = importSettings.ScaleFactor;
					}
				}
			);
			Repaint();
		}

		private void DrawPathPicker()
		{
			using (new GUILayout.HorizontalScope())
			{
				//EditorGUILayout.PrefixLabel("Import Path");

				string displayPath = importSettings.TargetDirectory;

				EditorGUI.BeginChangeCheck();
				string userPath = EditorGUILayout.DelayedTextField(labelPath, displayPath, GUILayout.ExpandWidth(true));
				if (EditorGUI.EndChangeCheck())
				{
					if (string.IsNullOrEmpty(userPath) == false)
						userPath = string.Format("Assets/{0}", userPath);
					else
						userPath = importPath.Substring(0, importPath.LastIndexOf("/"));
					if (AssetDatabase.IsValidFolder(userPath))
						importSettings.TargetDirectory = userPath;
				}

				if (GUILayout.Button(labelPickPath, EditorStyles.miniButtonRight, noExpandW))
				{
					GUI.FocusControl(null);
					string openPath = importSettings.TargetDirectory;
					if (string.IsNullOrEmpty(openPath))
						openPath = importPath.Substring(0, importPath.LastIndexOf("/"));

					openPath = EditorUtility.SaveFolderPanel("Export Path", openPath, "");

					int inPath = openPath.IndexOf(Application.dataPath);
					if (inPath < 0 || Application.dataPath.Length == openPath.Length)
						openPath = string.Empty;
					else
						openPath = openPath.Substring(Application.dataPath.LastIndexOf("/") + 1);

					importSettings.TargetDirectory = openPath;
				}
			}
		}

		private void RecursiveQuickSelect(ImportLayerData layer, bool inSelection)
		{
			SetQuickSelect(layer.indexId, inSelection);
			layer.Iterate(
				childLayer =>
				{
					if (layer.Childs == null)
						return;
					if (childLayer.import)
						SetQuickSelect(childLayer.indexId, inSelection);
				}
			);
			GetSelectCount();
		}

		private void SetQuickSelect(int[] layerIdx, bool inSelection)
		{
			bool layerInSelection = quickSelect.Contains(layerIdx);
			if (inSelection)
			{
				if (layerInSelection == false)
					quickSelect.Add(layerIdx);
			}
			else
			{
				if (layerInSelection)
					quickSelect.Remove(layerIdx);
			}
		}

		private void AddQuickSelect()
		{
			foreach (int[] layerIdx in quickSelect)
			{
				var setLayer = GetLayerData(layerIdx);
				if (setLayer == null)
					continue;
				setLayer.import = true;
			}
			quickSelect.Clear();
		}

		private bool ParentWillImport(int[] layerIdx)
		{
			bool willImport = true;
			ImportLayerData currentLayer = importSettings.DocRoot;
			for (int i = 0; i < layerIdx.Length - 1; i++)
			{
				int idx = layerIdx[i];
				currentLayer = currentLayer.Childs[idx];
				willImport &= currentLayer.import;
				if (willImport == false)
					break;
			}
			return willImport;
		}

		private void CollateImportList()
		{
			importLayersList.Clear();
			importSettings.DocRoot.Iterate(
				layerCallback: layer =>
				{
					if (layer.import && layer.Childs.Count == 0)
						importLayersList.Add(layer.indexId);
				},
				canEnterGroup: layer => layer.import
			);
		}

		private void GetSelectCount()
		{
			selectionCount = 0;
			foreach (int[] layerIdx in quickSelect)
			{
				var layer = GetLayerData(layerIdx);
				if (layer == null)
					continue;
				if (layer.Childs.Count == 0)
					selectionCount++;
			}
		}

		private DisplayLayerData GetDisplayData(int[] layerIdx)
		{
			if (importDisplay == null || layerIdx == null)
				return null;

			DisplayLayerData currentLayer = importDisplay;
			foreach (int idx in layerIdx)
			{
				if (idx < 0 || idx >= currentLayer.Childs.Count)
					return null;
				currentLayer = currentLayer.Childs[idx];
			}
			return currentLayer;
		}

		private ImportLayerData GetLayerData(int[] layerIdx)
		{
			if (importSettings == null || layerIdx == null)
				return null;

			return importSettings.GetLayerData(layerIdx);
		}

		private void WriteImportSettings()
		{

			// Read the texture import settings of the asset file
			TextureImporter textureImporter = (TextureImporter)AssetImporter.GetAtPath(importPath);
			TextureImporterSettings settings = new TextureImporterSettings();
			textureImporter.ReadTextureSettings(settings);

			settings.spritePixelsPerUnit = importPPU;

			textureImporter.SetTextureSettings(settings);

			fsData data;
			if (serializer.TrySerialize(typeImportUserData, importSettings, out data)
				.AssertSuccessWithoutWarnings()
				.Succeeded)
			{
				textureImporter.userData = fsJsonPrinter.CompressedJson(data);
			}

			EditorUtility.SetDirty(importFile);
			AssetDatabase.WriteImportSettingsIfDirty(importPath);
			AssetDatabase.ImportAsset(importPath, ImportAssetOptions.ForceUpdate);

			settingsChanged = false;
			CollateImportList();
		}

		private void InitializeSortingLayerOfGroup(ImportLayerData importLayerData)
        {
            char split = ' ';

            string targetName = importLayerData.name;

			string[] splits = targetName.Split(split);

			List<string> result = new List<string>();

			foreach (var sortingLayer in UnityEngine.SortingLayer.layers)
			{
				result.Add(sortingLayer.name.Replace(" ", string.Empty).ToLower());
			}

			foreach(var iter in splits)
				result = SearchString(iter.ToLower(), result);

			if(result.Count > 0)
			{
				foreach (var sortingLayer in SortingLayer.layers)
				{
					if(sortingLayer.name.Replace(" ", string.Empty).ToLower() == result[0])
					{
						importLayerData.sortingLayer = sortingLayer.value;
						return;
					}
				}
			}
		}


        private bool SearchString(string str, string findStr)
        {
            if (str == null || findStr == null)
                return false;

            int found = 0, total = 0;

            for (int i = 0; i < str.Length; i++)
            {
                found = str.IndexOf(findStr, i, System.StringComparison.CurrentCulture);
                if (found >= 0)
                {
                    total++;
                    i = found;
                }
            }

            return total > 0 ? true : false;
        }

		private List<String> SearchString(string str, List<String> searchingList)
		{
			List<string> result = new List<string>();

			foreach(var iter in searchingList)
			{
				if(SearchString(iter, str))
				{
					result.Add(iter);
				}
			}

			return result;
		}
    }
}