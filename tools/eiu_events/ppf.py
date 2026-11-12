"""PPF 3.0 patches, the format PS1 hacks are shared in: read one, and apply it to a copy of a disc image.

    apply_ppf(read_ppf("hack.ppf"), "vanilla.bin", "hack.bin")
"""

from __future__ import annotations

import shutil
from dataclasses import dataclass
from pathlib import Path

MAGIC = b"PPF30"
HEADER_SIZE = 0x3C
VALIDATION_SIZE = 1024
# Where the validation block is compared against the source image, by image type.
VALIDATION_OFFSET = {0: 0x9320, 1: 0x80A0}
IMAGE_TYPES = {0: "BIN", 1: "GI"}
BEGIN_ID = b"@BEGIN_FILE_ID.DIZ"
END_ID = b"@END_FILE_ID.DIZ"


@dataclass(frozen=True)
class PpfRecord:
    offset: int
    data: bytes


@dataclass(frozen=True)
class PpfPatch:
    description: str
    image_type: int
    validation: bytes | None
    has_undo: bool
    records: list[PpfRecord]
    file_id: str | None

    @property
    def bytes_written(self) -> int:
        return sum(len(r.data) for r in self.records)

    @property
    def end_offset(self) -> int:
        return max((r.offset + len(r.data) for r in self.records), default=0)


def read_ppf(path: str | Path) -> PpfPatch:
    buf = Path(path).read_bytes()
    if buf[:5] != MAGIC:
        raise ValueError(f"{path}: not a PPF 3.0 patch (starts with {buf[:5]!r})")

    description = buf[6:56].decode("latin-1").rstrip(" \x00")
    image_type, block_check, undo = buf[0x38], buf[0x39], buf[0x3A]

    pos = HEADER_SIZE
    validation = None
    if block_check:
        validation = buf[pos : pos + VALIDATION_SIZE]
        pos += VALIDATION_SIZE

    # An optional FILE_ID.DIZ text block sits after the records.
    end, file_id = len(buf), None
    begin = buf.rfind(BEGIN_ID)
    end_marker = buf.rfind(END_ID)
    if begin != -1 and end_marker > begin and end_marker >= len(buf) - len(END_ID) - 4:
        file_id = buf[begin + len(BEGIN_ID) : end_marker].decode("latin-1").strip()
        end = begin

    records = []
    while pos < end:
        if pos + 9 > end:
            raise ValueError(f"{path}: truncated record at byte {pos}")
        offset = int.from_bytes(buf[pos : pos + 8], "little")
        length = buf[pos + 8]
        pos += 9
        data = buf[pos : pos + length]
        pos += length * (2 if undo else 1)  # undo bytes follow the data when present
        if len(data) != length or pos > end:
            raise ValueError(f"{path}: truncated record at offset {offset:#x}")
        records.append(PpfRecord(offset, data))

    return PpfPatch(description, image_type, validation, bool(undo), records, file_id)


def apply_ppf(patch: PpfPatch, source: str | Path, dest: str | Path) -> None:
    """Copy source to dest and write the patch into dest. Source is never changed."""
    source, dest = Path(source), Path(dest)
    if source.resolve() == dest.resolve():
        raise ValueError("source and destination must be different files")

    if patch.validation is not None:
        at = VALIDATION_OFFSET.get(patch.image_type)
        if at is None:
            raise ValueError(f"unknown PPF image type {patch.image_type}")
        with open(source, "rb") as f:
            f.seek(at)
            if f.read(VALIDATION_SIZE) != patch.validation:
                raise ValueError(f"{source.name} is not the image this patch was made for")

    shutil.copyfile(source, dest)
    with open(dest, "r+b") as f:
        for record in patch.records:  # order matters: later records win
            f.seek(record.offset)
            f.write(record.data)
