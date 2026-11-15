"""Hand-placed pixel art for the small tray sizes (16, 20, 24, 32 px).

The tray draws the icon at 16 px at 100 % scale, 20 px at 125 %, 24 px at 150 %, 32 px at 200 %. At those sizes a
downscaled drawing turns to mush, so each size is drawn pixel by pixel here: a sonic screwdriver on the diagonal,
a glowing blue emitter at the top right, and sound arcs. make_icon.py puts these frames in app.ico next to the
smooth drawing used from 40 px up.

One character per pixel. The rows of each size must be exactly size x size.
"""

PALETTE = {
    ".": (0, 0, 0, 0),
    "K": (12, 16, 24, 255),      # outline
    "G": (62, 70, 88, 255),      # grip
    "R": (132, 142, 160, 255),   # grip ring
    "H": (112, 120, 136, 255),   # collar
    "S": (196, 204, 216, 255),   # steel
    "W": (240, 248, 255, 255),   # hot core
    "B": (64, 170, 255, 255),    # emitter / sound
    "D": (30, 112, 214, 255),    # emitter rim / sound shade
}

ART = {
    16: [
        "................",
        "...........DDD..",
        ".............D..",
        ".........DBD..D.",
        "........DWWBD.D.",
        "........DBBBD...",
        ".........DBD....",
        "........SS......",
        ".......SS.......",
        ".....HHH........",
        "....HHH.........",
        "...KGGGK........",
        "..KRGGK.........",
        ".KGGGK..........",
        "KGRGK...........",
        "KKKK............",
    ],
    20: [
        "..............DD....",
        "................DD..",
        "..................D.",
        ".............DBD...D",
        "............DBBBD..D",
        "...........DBWWBBD.D",
        "...........DBWBBBD..",
        "...........DBBBBBD..",
        "............DBBBD...",
        ".............DDD....",
        "..........SS........",
        ".........SS.........",
        "........SS..........",
        "......HHHH..........",
        ".....HHHH...........",
        "...KGGGK............",
        "..KRGGK.............",
        ".KGGGK..............",
        "KGRGK...............",
        "KKKK................",
    ],
    24: [
        "................DDD.....",
        "...................DD...",
        ".....................D..",
        "................DBD...D.",
        "...............DBWBD...D",
        "..............DBWBBBD..D",
        "..............DBBBBBD...",
        "..............DBBBBBD...",
        "...............DBBBD....",
        "................DDD.....",
        ".............WSS........",
        "............WSS.........",
        "...........WSS..........",
        "..........WSS...........",
        "........HHHHH...........",
        ".......HHHHH............",
        "......HHHHH.............",
        "....KGGGGGK.............",
        "...KRGGGGK..............",
        "..KGGGGGK...............",
        ".KGGRGGK................",
        "KGGGGGK.................",
        "KGGRGK..................",
        "KKKKK...................",
    ],
    32: [
        ".....................BBBB.......",
        "......................DDDBBB....",
        "..........................DDB...",
        "............................DB..",
        ".............................DB.",
        "....................DDDDD....DB.",
        "...................DBBBBBD....DB",
        "..................DBWWBBBBD...DB",
        "..................DBWBBBBBD...DB",
        "..................DBBBBBBBD...DB",
        "..................DBBBBBBBD.....",
        "..................DBBBBBBBD.....",
        "...................DBBBBBD......",
        "....................DDDDD.......",
        ".................WSSS...........",
        "................WSSS............",
        "...............WSSS.............",
        "..............WSSS..............",
        ".............WSSS...............",
        "............WSSS................",
        "..........HHHHHH................",
        ".........HHHHHH.................",
        "........HHHHHH..................",
        ".......HHHHHH...................",
        ".....KGGGGGGK...................",
        "....KRGGGGGK....................",
        "...KGGGGGGK.....................",
        "..KGGRGGGK......................",
        ".KGGGGGGK.......................",
        "KGGGRGGK........................",
        "KGGGGGK.........................",
        "KKKKK...........................",
    ],
}


def frame(size):
    """The art for a size as (width, height, list of RGBA tuples, row by row)."""
    rows = ART[size]
    assert len(rows) == size and all(len(r) == size for r in rows), f"{size}: rows must be {size}x{size}"
    return [PALETTE[c] for r in rows for c in r]
