#!/usr/bin/env python3
"""虚拟串口设备模拟器：持有一对 pty，把 slave 路径写给被测程序。
收到的任何字节都会记录到 /tmp/device_rx.log，并回一帧 AA 81 00 55 模拟设备应答。"""
import os
import pty
import select
import sys

master, slave = pty.openpty()
slave_name = os.ttyname(slave)

with open('/tmp/pty_slave.txt', 'w') as f:
    f.write(slave_name)

print(slave_name, flush=True)

rx = open('/tmp/device_rx.log', 'w', buffering=1)

while True:
    try:
        r, _, _ = select.select([master], [], [], 0.2)
        if not r:
            continue
        data = os.read(master, 4096)
        if not data:
            break
        rx.write(data.hex(' ').upper() + '\n')
        os.write(master, bytes.fromhex('AA810055'))
    except (OSError, KeyboardInterrupt):
        break
