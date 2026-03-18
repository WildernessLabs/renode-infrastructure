//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//

namespace Antmicro.Renode.Peripherals.UART
{
    // Optional interface for UART peripherals that support efficient bulk writes.
    // When implemented, BackendTerminal.WriteBufferToUART will call WriteChars
    // instead of WriteChar per byte, allowing the peripheral to defer expensive
    // per-byte operations (IRQ updates, idle line scheduling) to a single
    // operation after all bytes are enqueued.
    public interface IBulkWriteUART
    {
        void WriteChars(byte[] data, int offset, int count);
    }
}
