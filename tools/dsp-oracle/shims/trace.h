#ifndef XGRID_TRACE_SHIM_H
#define XGRID_TRACE_SHIM_H
#define TRACE_DSP56K_EXECUTE_INSTRUCTION_DISASM 0
#define trace_event_get_state(x) (false)
#define trace_dsp56k_execute_instruction(a, b) ((void)0)
#define trace_dsp56k_execute_instruction_disasm(a) ((void)0)
#define trace_dsp_read_peripheral(a, b) ((void)0)
#define trace_dsp_write_peripheral(a, b) ((void)0)
#endif
