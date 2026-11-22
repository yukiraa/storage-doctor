//! Native scan engine. Only progress, totals and the largest 100 paths cross
//! into the UI. Windows path names remain UTF-16 throughout the interface.
use std::{
    cmp::Reverse,
    collections::BTreeSet,
    ffi::OsString,
    fs, io,
    os::windows::{
        ffi::{OsStrExt, OsStringExt},
        fs::MetadataExt,
    },
    path::Path,
    time::{Duration, Instant},
};

const REPARSE_POINT: u32 = 0x400;
const LIMIT: usize = 100;
type RankedFile = (Reverse<u64>, Vec<u16>);

#[derive(Default)]
struct Report {
    files: u64,
    bytes: u64,
    skipped: u64,
    largest: BTreeSet<RankedFile>,
}

impl Report {
    fn add_file(&mut self, entry: &fs::DirEntry, bytes: u64) {
        self.files += 1;
        self.bytes += bytes;
        if self.largest.len() == LIMIT && self.largest.last().unwrap().0 .0 > bytes {
            return;
        }
        let path = entry.path().as_os_str().encode_wide().collect();
        self.largest.insert((Reverse(bytes), path));
        if self.largest.len() > LIMIT {
            self.largest.pop_last();
        }
    }
}

fn open_dir(path: &Path) -> io::Result<fs::ReadDir> {
    if fs::symlink_metadata(path)?.file_attributes() & REPARSE_POINT != 0 {
        return Err(io::Error::other(
            "Choose the local target of this link or cloud placeholder.",
        ));
    }
    fs::read_dir(path)
}

type Progress = extern "C" fn(u64) -> i32;
type Row = extern "C" fn(u64, *const u16, i32);

fn scan(root: &Path, progress: Progress) -> io::Result<Report> {
    let mut report = Report::default();
    let mut pending = vec![root.to_owned()];
    let mut last_update = Instant::now();
    while let Some(dir) = pending.pop() {
        if last_update.elapsed() >= Duration::from_millis(100) {
            if progress(report.files) == 0 {
                return Err(io::Error::from_raw_os_error(1223));
            }
            last_update = Instant::now();
        }
        let entries = match open_dir(&dir) {
            Ok(entries) => entries,
            Err(err) if dir == root => return Err(err),
            Err(_) => {
                report.skipped += 1;
                continue;
            }
        };
        for item in entries {
            if last_update.elapsed() >= Duration::from_millis(100) {
                if progress(report.files) == 0 {
                    return Err(io::Error::from_raw_os_error(1223));
                }
                last_update = Instant::now();
            }
            // Windows supplies attributes and length in the directory entry.
            // No file content reads, and no full path allocation for small files.
            let Ok(entry) = item else {
                report.skipped += 1;
                continue;
            };
            let Ok(meta) = entry.metadata() else {
                report.skipped += 1;
                continue;
            };
            if meta.file_attributes() & REPARSE_POINT != 0 {
                report.skipped += 1;
            } else if meta.is_dir() {
                pending.push(entry.path());
            } else {
                report.add_file(&entry, meta.file_size());
            }
        }
    }
    Ok(report)
}

/// # Safety
/// The caller provides a valid UTF-16 path of `len` units and three writable
/// 64-bit counters. Callback pointers are valid until the call returns.
#[no_mangle]
pub unsafe extern "C" fn scan_folder(
    path: *const u16,
    len: i32,
    progress: Progress,
    row: Row,
    totals: *mut [u64; 3],
) -> i32 {
    let root = OsString::from_wide(unsafe { std::slice::from_raw_parts(path, len as usize) });
    match scan(Path::new(&root), progress) {
        Ok(report) => {
            for (Reverse(size), path) in &report.largest {
                row(*size, path.as_ptr(), path.len() as i32);
            }
            unsafe { *totals = [report.files, report.bytes, report.skipped] };
            0
        }
        Err(err) => err.raw_os_error().unwrap_or(31),
    }
}
