/*
 * C FFI for Sudachi mode-C tokenization (surface forms, one token per line in UTF-8).
 * One SudachiContext per thread — not thread-safe across threads.
 */
use std::ffi::{CStr, CString};
use std::os::raw::{c_char, c_int};
use std::path::Path;
use std::ptr;
use std::slice;
use std::sync::Arc;

use sudachi::analysis::stateful_tokenizer::StatefulTokenizer;
use sudachi::config::Config;
use sudachi::dic::dictionary::JapaneseDictionary;
use sudachi::prelude::*;

thread_local! {
    static LAST_ERROR: std::cell::RefCell<Option<CString>> = const { std::cell::RefCell::new(None) };
}

fn set_error(msg: impl Into<Vec<u8>>) {
    LAST_ERROR.with(|cell| {
        *cell.borrow_mut() = Some(
            CString::new(msg).unwrap_or_else(|_| CString::new("invalid error message").unwrap()),
        );
    });
}

fn clear_error() {
    LAST_ERROR.with(|cell| *cell.borrow_mut() = None);
}

type SharedDict = Arc<JapaneseDictionary>;

pub struct SudachiContext {
    _dict: SharedDict,
    tokenizer: StatefulTokenizer<SharedDict>,
    morphemes: MorphemeList<SharedDict>,
}

fn load_dictionary(resource_dir: &Path, dict_path: &Path) -> Result<JapaneseDictionary, c_int> {
    let config_file = resource_dir.join("sudachi.json");
    let config = if config_file.exists() {
        Config::new(
            Some(config_file),
            Some(resource_dir.to_path_buf()),
            Some(dict_path.to_path_buf()),
        )
    } else {
        Ok(Config::minimal_at(resource_dir).with_system_dic(dict_path))
    }
    .map_err(|e| {
        set_error(format!("config load failed: {e:?}"));
        -5
    })?;
    JapaneseDictionary::from_cfg(&config).map_err(|e| {
        set_error(format!("dictionary load failed: {e:?}"));
        -5
    })
}

/// Create a tokenizer context. `resource_dir` must contain char.def; `dict_path` is system_core.dic.
#[no_mangle]
pub extern "C" fn sudachi_create(resource_dir: *const c_char, dict_path: *const c_char) -> *mut SudachiContext {
    clear_error();
    if resource_dir.is_null() || dict_path.is_null() {
        set_error("resource_dir or dict_path is null");
        return ptr::null_mut();
    }
    let resource_dir = unsafe { CStr::from_ptr(resource_dir) };
    let dict_path = unsafe { CStr::from_ptr(dict_path) };
    let resource_dir = match resource_dir.to_str() {
        Ok(s) => Path::new(s),
        Err(_) => {
            set_error("resource_dir is not valid UTF-8");
            return ptr::null_mut();
        }
    };
    let dict_path = match dict_path.to_str() {
        Ok(s) => Path::new(s),
        Err(_) => {
            set_error("dict_path is not valid UTF-8");
            return ptr::null_mut();
        }
    };

    let dict = match load_dictionary(resource_dir, dict_path) {
        Ok(d) => Arc::new(d),
        Err(_) => return ptr::null_mut(),
    };

    let morphemes = MorphemeList::empty(Arc::clone(&dict));
    let tokenizer = StatefulTokenizer::create(Arc::clone(&dict), false, Mode::C);
    Box::into_raw(Box::new(SudachiContext {
        _dict: dict,
        tokenizer,
        morphemes,
    }))
}

#[no_mangle]
pub extern "C" fn sudachi_destroy(ctx: *mut SudachiContext) {
    if !ctx.is_null() {
        unsafe { drop(Box::from_raw(ctx)); }
    }
}

/// Tokenize UTF-8 text. Writes surfaces separated by `\n` into `out_buf`.
/// Returns 0 on success; -4 if buffer too small (`out_len` = required size including trailing `\0`).
#[no_mangle]
pub extern "C" fn sudachi_tokenize(
    ctx: *mut SudachiContext,
    text: *const c_char,
    out_buf: *mut u8,
    out_cap: usize,
    out_len: *mut usize,
) -> c_int {
    clear_error();
    if ctx.is_null() {
        set_error("context is null");
        return -1;
    }
    if text.is_null() {
        set_error("text is null");
        return -2;
    }
    if out_len.is_null() {
        set_error("out_len is null");
        return -2;
    }

    let text = unsafe { CStr::from_ptr(text) };
    let text = match text.to_str() {
        Ok(s) => s,
        Err(_) => {
            set_error("text is not valid UTF-8");
            return -3;
        }
    };

    let ctx = unsafe { &mut *ctx };
    ctx.tokenizer.reset().push_str(text);
    if let Err(e) = ctx.tokenizer.do_tokenize() {
        set_error(format!("tokenize failed: {e:?}"));
        return -6;
    }
    if let Err(e) = ctx.morphemes.collect_results(&mut ctx.tokenizer) {
        set_error(format!("collect_results failed: {e:?}"));
        return -6;
    }

    let mut output = String::new();
    let mut first = true;
    for m in ctx.morphemes.iter() {
        if !first {
            output.push('\n');
        }
        first = false;
        output.push_str(&m.surface());
    }

    let bytes = output.as_bytes();
    let required = bytes.len() + 1;
    unsafe { *out_len = required };

    if out_buf.is_null() || out_cap < required {
        return -4;
    }

    unsafe {
        let out = slice::from_raw_parts_mut(out_buf, out_cap);
        out[..bytes.len()].copy_from_slice(bytes);
        out[bytes.len()] = 0;
    }
    0
}

#[no_mangle]
pub extern "C" fn sudachi_last_error() -> *const c_char {
    LAST_ERROR.with(|cell| match cell.borrow().as_ref() {
        Some(s) => s.as_ptr(),
        None => ptr::null(),
    })
}
