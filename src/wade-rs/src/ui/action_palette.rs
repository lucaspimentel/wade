//! Port of `src/Wade/UI/ActionMenuItem.cs` and `src/Wade/UI/ActionMenuLevel.cs`:
//! the action palette data model.

use crate::app::input_reader::AppAction;
use crate::ui::text_input::TextInput;

#[derive(Clone)]
pub struct ActionMenuItem {
    pub label: String,
    pub shortcut: String,
    pub action: AppAction,
    pub data: i32,
    pub sub_items: Option<Vec<ActionMenuItem>>,
}

impl ActionMenuItem {
    #[must_use]
    pub fn new(label: &str, shortcut: &str, action: AppAction) -> Self {
        Self {
            label: label.to_string(),
            shortcut: shortcut.to_string(),
            action,
            data: 0,
            sub_items: None,
        }
    }

    #[must_use]
    pub fn submenu(label: &str, shortcut: &str, sub_items: Vec<ActionMenuItem>) -> Self {
        Self {
            label: label.to_string(),
            shortcut: shortcut.to_string(),
            action: AppAction::None,
            data: 0,
            sub_items: Some(sub_items),
        }
    }

    #[must_use]
    pub fn is_submenu(&self) -> bool {
        self.sub_items.is_some()
    }
}

pub struct ActionMenuLevel {
    pub title: String,
    pub items: Vec<ActionMenuItem>,
    pub filter: TextInput,
    pub selected_index: usize,
    pub scroll_offset: usize,
}

impl ActionMenuLevel {
    #[must_use]
    pub fn new(title: &str, items: Vec<ActionMenuItem>) -> Self {
        Self {
            title: title.to_string(),
            items,
            filter: TextInput::default(),
            selected_index: 0,
            scroll_offset: 0,
        }
    }

    /// Port of `GetFilteredItems`: label substring filter, case-insensitive.
    #[must_use]
    pub fn get_filtered_items(&self) -> Vec<&ActionMenuItem> {
        let filter = self.filter.value();
        if filter.is_empty() {
            return self.items.iter().collect();
        }

        let filter_upper = filter.to_uppercase();
        self.items
            .iter()
            .filter(|item| item.label.to_uppercase().contains(&filter_upper))
            .collect()
    }
}
