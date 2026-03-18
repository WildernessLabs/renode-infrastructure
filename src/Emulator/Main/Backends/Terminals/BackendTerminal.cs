//
// Copyright (c) 2010-2026 Antmicro
// Copyright (c) 2011-2015 Realtime Embedded
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Collections;
using System.Linq;

using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals;
using Antmicro.Renode.Peripherals.UART;
using Antmicro.Renode.Time;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Backends.Terminals
{
    public abstract class BackendTerminal : IExternal, IConnectable<IUART>
    {
        public BackendTerminal()
        {
            buffer = new Queue();
        }

        public virtual void BufferStateChanged(BufferState state)
        {
            lock(innerLock)
            {
                if(state == BufferState.Full || pendingTimeDomainEvent)
                {
                    return;
                }
                pendingTimeDomainEvent = true;
            }
            HandleExternalTimeDomainEvent<object>(_ => WriteBufferToUART(), null);
        }

        public virtual void AttachTo(IUART uart)
        {
            this.uart = uart;
            this.machine = uart.GetMachine();

            var uartWithBuffer = uart as IUARTWithBufferState;
            if(uartWithBuffer != null)
            {
                Antmicro.Renode.Logging.Logger.LogAs(uart, Antmicro.Renode.Logging.LogLevel.Warning,
                    "BackendTerminal: Using BATCHED write path (IUARTWithBufferState)");
                CharReceived += EnqueueWriteToUART;
                uartWithBuffer.BufferStateChanged += BufferStateChanged;
            }
            else
            {
                Antmicro.Renode.Logging.Logger.LogAs(uart, Antmicro.Renode.Logging.LogLevel.Warning,
                    "BackendTerminal: Using PER-BYTE write path (no IUARTWithBufferState)");
                CharReceived += WriteToUART;
            }

            uart.CharReceived += WriteChar;
        }

        public virtual void DetachFrom(IUART uart)
        {
            var uartWithBuffer = uart as IUARTWithBufferState;
            if(uartWithBuffer != null)
            {
                CharReceived -= EnqueueWriteToUART;
                uartWithBuffer.BufferStateChanged -= BufferStateChanged;
            }
            else
            {
                CharReceived -= WriteToUART;
            }

            uart.CharReceived -= WriteChar;

            this.uart = null;
            this.machine = null;
            buffer.Clear();
        }

        public abstract void WriteChar(byte value);

        public virtual event Action<byte> CharReceived;

        protected void CallCharReceived(byte value)
        {
            var charReceived = CharReceived;
            if(charReceived != null)
            {
                charReceived(value);
            }
        }

        private void EnqueueWriteToUART(byte value)
        {
            lock(innerLock)
            {
                buffer.Enqueue(value);
                if(!pendingTimeDomainEvent)
                {
                    pendingTimeDomainEvent = true;
                    HandleExternalTimeDomainEvent<object>(_ => WriteBufferToUART(), null);
                }
            }
        }

        private void WriteBufferToUART()
        {
            lock(innerLock)
            {
                var uartWithBuffer = uart as IUARTWithBufferState;

                // Convert buffer to array for potential bulk write
                var count = buffer.Count;
                if(count > 10)
                {
                    Antmicro.Renode.Logging.Logger.LogAs(uart, Antmicro.Renode.Logging.LogLevel.Warning,
                        "WriteBufferToUART: {0} bytes, type={1}, interfaces={2}", count, uart.GetType().FullName,
                        string.Join(",", uart.GetType().GetInterfaces().Select(i => i.Name)));
                }
                if(count > 0)
                {
                    var data = new byte[count];
                    for(int i = 0; i < count && uartWithBuffer.BufferState != BufferState.Full; i++)
                    {
                        data[i] = (byte)buffer.Dequeue();
                    }
                    // Use bulk WriteChars if available (avoids per-byte idle line
                    // scheduling and IRQ updates — critical for high-throughput transfers)
                    var bulkWriter = uart as IBulkWriteUART;
                    if(bulkWriter != null)
                    {
                        bulkWriter.WriteChars(data, 0, count);
                    }
                    else
                    {
                        for(int i = 0; i < count; i++)
                        {
                            uart.WriteChar(data[i]);
                        }
                    }
                }

                pendingTimeDomainEvent = false;
            }
        }

        private void WriteToUART(byte value)
        {
            HandleExternalTimeDomainEvent(uart.WriteChar, value);
        }

        private void HandleExternalTimeDomainEvent<T>(Action<T> handler, T handlerValue)
        {
            var vts = TimeDomainsManager.Instance.GetEffectiveVirtualTimeStamp();
            machine.HandleTimeDomainEvent(handler, handlerValue, vts);
        }

        private IUART uart;
        private IMachine machine;
        private bool pendingTimeDomainEvent;

        private readonly Queue buffer;
        private readonly object innerLock = new object();
    }
}