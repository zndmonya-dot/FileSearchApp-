#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Sudachi 形態素解析（モード C）のトークン列を標準出力に 1 行 1 トークンで出力する。

通常モード: 標準入力から全文を読み、トークン化して終了（1 ドキュメントのみ）。
ストリームモード (--stream): 行単位で読み、区切り行 "---SUDACHI_DOC_END---" までを 1 ドキュメントとして
  トークン化し、トークン行＋区切り行を出力。プロセスを維持して複数ドキュメントを連続処理（高速化）。
"""
import sys
from sudachipy import tokenizer, dictionary

DELIM = "---SUDACHI_DOC_END---"


def run_stream_mode(tokenizer_obj, mode):
    buffer = []
    for line in sys.stdin:
        line = line.rstrip("\n\r")
        if line == DELIM:
            text = "\n".join(buffer)
            buffer = []
            if text.strip():
                for m in tokenizer_obj.tokenize(text, mode):
                    surface = m.surface()
                    if surface:
                        print(surface)
            print(DELIM, flush=True)
        else:
            buffer.append(line)
    if buffer:
        text = "\n".join(buffer)
        if text.strip():
            for m in tokenizer_obj.tokenize(text, mode):
                surface = m.surface()
                if surface:
                    print(surface)


def run_oneshot(tokenizer_obj, mode):
    text = sys.stdin.buffer.read().decode("utf-8", errors="replace").strip()
    if not text:
        return
    for m in tokenizer_obj.tokenize(text, mode):
        surface = m.surface()
        if surface:
            print(surface)


def main():
    tokenizer_obj = dictionary.Dictionary().create()
    mode = tokenizer.Tokenizer.SplitMode.C
    if len(sys.argv) > 1 and sys.argv[1] == "--stream":
        run_stream_mode(tokenizer_obj, mode)
    else:
        run_oneshot(tokenizer_obj, mode)


if __name__ == "__main__":
    main()
