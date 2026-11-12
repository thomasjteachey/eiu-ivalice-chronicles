"""Files out of a raw PS1 disc image (.bin with 2352-byte sectors): the ISO 9660 tree, a file's bytes, and which
file owns a sector.

    with Disc("hack.bin") as disc:
        test_evt = disc.read(disc.find("EVENT/TEST.EVT"))
"""

from __future__ import annotations

import bisect
import mmap
from dataclasses import dataclass
from pathlib import Path

SECTOR_SIZE = 2352
MODE2_DATA_START = 24  # 12 sync + 4 header + 8 subheader
FORM1_DATA = 2048
FORM2_DATA = 2324


def _u32(buf: bytes, at: int) -> int:
    return int.from_bytes(buf[at : at + 4], "little")


@dataclass(frozen=True)
class DiscEntry:
    path: str  # "SCUS_942.21", "BATTLE/ENTD1.ENT"; directories end in "/"
    lba: int
    size: int
    is_dir: bool = False

    @property
    def sector_count(self) -> int:
        return max(1, -(-self.size // FORM1_DATA))


class Disc:
    def __init__(self, path: str | Path):
        self.path = Path(path)
        self._file = open(self.path, "rb")
        self._map = mmap.mmap(self._file.fileno(), 0, access=mmap.ACCESS_READ)
        if len(self._map) % SECTOR_SIZE:
            self.close()
            raise ValueError(f"{self.path.name} is not a raw image with 2352-byte sectors")
        self.entries = self._read_tree()
        self._starts = [e.lba for e in self.entries]

    def close(self) -> None:
        self._map.close()
        self._file.close()

    def __enter__(self) -> Disc:
        return self

    def __exit__(self, *exc) -> None:
        self.close()

    @property
    def raw(self) -> mmap.mmap:
        return self._map

    @property
    def size(self) -> int:
        return len(self._map)

    def data_span(self, lba: int) -> tuple[int, int]:
        """Where the user data sits inside a raw sector: (start, length)."""
        base = lba * SECTOR_SIZE
        if self._map[base + 15] == 1:
            return 16, FORM1_DATA
        form2 = self._map[base + 18] & 0x20
        return MODE2_DATA_START, FORM2_DATA if form2 else FORM1_DATA

    def user_data(self, lba: int) -> bytes:
        start, length = self.data_span(lba)
        base = lba * SECTOR_SIZE + start
        return self._map[base : base + length]

    def read(self, entry: DiscEntry) -> bytes:
        """A file's bytes, assuming Form 1 sectors (true for everything but XA audio/video)."""
        chunks = (self.user_data(entry.lba + i)[:FORM1_DATA] for i in range(entry.sector_count))
        return b"".join(chunks)[: entry.size]

    def find(self, path: str) -> DiscEntry | None:
        want = path.replace("\\", "/").upper()
        return next((e for e in self.entries if e.path.upper() == want), None)

    def owner(self, lba: int) -> DiscEntry | None:
        """The file or directory whose extent covers this sector, if any."""
        i = bisect.bisect_right(self._starts, lba) - 1
        if i >= 0 and lba < self.entries[i].lba + self.entries[i].sector_count:
            return self.entries[i]
        return None

    def _read_tree(self) -> list[DiscEntry]:
        pvd = self.user_data(16)
        if pvd[0] != 1 or pvd[1:6] != b"CD001":
            raise ValueError(f"{self.path.name} has no ISO 9660 volume descriptor at sector 16")
        root = pvd[156:190]
        entries: list[DiscEntry] = []
        self._walk(_u32(root, 2), _u32(root, 10), "", entries, set())
        entries.sort(key=lambda e: e.lba)
        return entries

    def _walk(self, lba: int, size: int, prefix: str, out: list[DiscEntry], seen: set[int]) -> None:
        if lba in seen:
            return
        seen.add(lba)
        out.append(DiscEntry(prefix or "/", lba, size, is_dir=True))
        for i in range(-(-size // FORM1_DATA)):
            block = self.user_data(lba + i)[:FORM1_DATA]
            pos = 0
            # Records never cross a sector; a zero length byte means the rest is padding.
            while pos < len(block) and block[pos]:
                record = block[pos : pos + block[pos]]
                pos += block[pos]
                name = record[33 : 33 + record[32]]
                if name in (b"\x00", b"\x01"):  # "." and ".."
                    continue
                path = prefix + name.decode("ascii", "replace").split(";")[0]
                child_lba, child_size = _u32(record, 2), _u32(record, 10)
                if record[25] & 0x02:
                    self._walk(child_lba, child_size, path + "/", out, seen)
                else:
                    out.append(DiscEntry(path, child_lba, child_size))
