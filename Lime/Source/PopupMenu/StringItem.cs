﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Lime.PopupMenu
{
	public class StringItem : MenuItem
	{
		MenuButton button;

		public BareEventHandler Activated;
		public Menu Submenu;

		public string Text
		{
			get { return button.Caption; }
			set { button.Caption = value; }
		}

		public StringItem(string text, BareEventHandler activated = null)
		{
			Activated = activated;
			Setup(text);
		}

		public StringItem(string text, string iconPath, BareEventHandler activated = null)
		{
			Activated = activated;
			Setup(text);
			button.ImagePath = iconPath;
		}

		public StringItem(string text, Menu submenu)
		{
			Submenu = submenu;
			Setup(text);
		}

		private void Setup(string text)
		{
			button = new MenuButton();
			button.ArrowVisible = Submenu != null;
			Text = text;
			Frame.AddNode(new ExpandSiblingsToParent());
			Frame.AddNode(button);
			button.Clicked += OnClick;
		}

		void OnClick(Button button)
		{
			if (Submenu != null) {
				Submenu.Show();
				Submenu.Frame.X += 50;
				Submenu.Hidden += Menu.Hide;
			} else {
				Menu.Hide();
			}
			if (Activated != null) {
				Activated();
			}
		}
	}
}
