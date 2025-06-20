package operations

import (
	"fmt"
	"log"
	"os"
	"os/exec"
	"runtime"
	"strings"
	"time"
)

// ProcessReport interface for process operations
type ProcessReport interface {
	GetConfig() ProcessConfig
	IncrementSuccess()
	IncrementFailed()
	AddError(error)
	SetTotalOps(int)
	AddChildPID(int)
}

type ProcessConfig struct {
	Count    int
	Interval time.Duration
	Dir      string
	Verbose  bool
	Command  string
}

// ExecuteChildProcess creates and manages child processes
func ExecuteChildProcess(report ProcessReport) error {
	config := report.GetConfig()
	report.SetTotalOps(config.Count)
	
	if config.Verbose {
		log.Printf("子プロセス作成開始: %d回、間隔 %v", config.Count, config.Interval)
	}

	for i := 0; i < config.Count; i++ {
		var cmd *exec.Cmd
		var cmdDesc string

		if config.Command != "" {
			// Custom command specified
			parts := strings.Fields(config.Command)
			if len(parts) > 0 {
				cmd = exec.Command(parts[0], parts[1:]...)
				cmdDesc = config.Command
			}
		} else {
			// Default platform-specific commands
			if runtime.GOOS == "windows" {
				cmd = exec.Command("cmd", "/c", fmt.Sprintf("echo Child process %d from PID %d && timeout /t 1 > nul", i+1, os.Getpid()))
				cmdDesc = "cmd /c echo + timeout"
			} else {
				cmd = exec.Command("sh", "-c", fmt.Sprintf("echo 'Child process %d from PID %d' && sleep 1", i+1, os.Getpid()))
				cmdDesc = "sh -c echo + sleep"
			}
		}

		if cmd == nil {
			err := fmt.Errorf("無効なコマンド: %s", config.Command)
			report.AddError(err)
			report.IncrementFailed()
			continue
		}

		if config.Verbose {
			log.Printf("子プロセス実行中 %d/%d: %s", i+1, config.Count, cmdDesc)
		}

		err := cmd.Start()
		if err != nil {
			report.AddError(fmt.Errorf("子プロセス開始エラー: %w", err))
			report.IncrementFailed()
			continue
		}

		childPID := cmd.Process.Pid
		report.AddChildPID(childPID)

		if config.Verbose {
			log.Printf("子プロセス開始: PID %d", childPID)
		}

		// Wait for the process to complete
		err = cmd.Wait()
		if err != nil {
			report.AddError(fmt.Errorf("子プロセス実行エラー PID %d: %w", childPID, err))
			report.IncrementFailed()
		} else {
			report.IncrementSuccess()
			if config.Verbose {
				log.Printf("子プロセス完了: PID %d", childPID)
			}
		}

		if i < config.Count-1 {
			time.Sleep(config.Interval)
		}
	}

	return nil
}

// ExecuteLongRunningProcess creates long-running child processes
func ExecuteLongRunningProcess(report ProcessReport) error {
	config := report.GetConfig()
	report.SetTotalOps(config.Count)
	
	if config.Verbose {
		log.Printf("長時間実行子プロセス作成開始: %d回、間隔 %v", config.Count, config.Interval)
	}

	var processes []*exec.Cmd

	// Start all processes
	for i := 0; i < config.Count; i++ {
		var cmd *exec.Cmd
		var cmdDesc string

		if runtime.GOOS == "windows" {
			// Windows: Use timeout command for long-running process
			cmd = exec.Command("cmd", "/c", fmt.Sprintf("timeout /t 10 > nul"))
			cmdDesc = "cmd /c timeout 10s"
		} else {
			// Unix: Use sleep command
			cmd = exec.Command("sleep", "10")
			cmdDesc = "sleep 10s"
		}

		if config.Verbose {
			log.Printf("長時間実行プロセス開始中 %d/%d: %s", i+1, config.Count, cmdDesc)
		}

		err := cmd.Start()
		if err != nil {
			report.AddError(fmt.Errorf("長時間実行プロセス開始エラー: %w", err))
			report.IncrementFailed()
			continue
		}

		childPID := cmd.Process.Pid
		report.AddChildPID(childPID)
		processes = append(processes, cmd)

		if config.Verbose {
			log.Printf("長時間実行プロセス開始: PID %d", childPID)
		}

		report.IncrementSuccess()

		if i < config.Count-1 {
			time.Sleep(config.Interval)
		}
	}

	// Wait a bit for processes to run
	if config.Verbose {
		log.Printf("プロセス実行中... (5秒待機)")
	}
	time.Sleep(5 * time.Second)

	// Kill all processes
	for _, cmd := range processes {
		if cmd != nil && cmd.Process != nil {
			if config.Verbose {
				log.Printf("プロセス終了中: PID %d", cmd.Process.Pid)
			}
			
			err := cmd.Process.Kill()
			if err != nil {
				if config.Verbose {
					log.Printf("プロセス終了エラー PID %d: %v", cmd.Process.Pid, err)
				}
			} else {
				if config.Verbose {
					log.Printf("プロセス終了完了: PID %d", cmd.Process.Pid)
				}
			}
		}
	}

	return nil
}

// ExecuteProcessTree creates a tree of child processes
func ExecuteProcessTree(report ProcessReport) error {
	config := report.GetConfig()
	report.SetTotalOps(config.Count)
	
	if config.Verbose {
		log.Printf("プロセスツリー作成開始: %d階層", config.Count)
	}

	// Create a script that creates child processes
	var cmd *exec.Cmd
	var cmdDesc string

	if runtime.GOOS == "windows" {
		// Windows: Create a batch script that spawns child processes
		script := fmt.Sprintf(`
@echo off
echo Parent process: %%*
for /L %%%%i in (1,1,%d) do (
    start /min cmd /c "echo Child %%%%i from %%* && timeout /t 2 > nul"
)
timeout /t 3 > nul
`, config.Count)
		
		// Write script to temp file
		scriptPath := fmt.Sprintf("%s\\proctail_tree_%d.bat", os.TempDir(), os.Getpid())
		err := os.WriteFile(scriptPath, []byte(script), 0644)
		if err != nil {
			return fmt.Errorf("スクリプト作成エラー: %w", err)
		}
		defer os.Remove(scriptPath)

		cmd = exec.Command("cmd", "/c", scriptPath, fmt.Sprintf("PID_%d", os.Getpid()))
		cmdDesc = "batch script with child processes"
	} else {
		// Unix: Create a shell script that spawns child processes
		script := fmt.Sprintf(`#!/bin/sh
echo "Parent process: $1"
for i in $(seq 1 %d); do
    (echo "Child $i from $1" && sleep 2) &
done
sleep 3
wait
`, config.Count)
		
		scriptPath := fmt.Sprintf("%s/proctail_tree_%d.sh", os.TempDir(), os.Getpid())
		err := os.WriteFile(scriptPath, []byte(script), 0755)
		if err != nil {
			return fmt.Errorf("スクリプト作成エラー: %w", err)
		}
		defer os.Remove(scriptPath)

		cmd = exec.Command("sh", scriptPath, fmt.Sprintf("PID_%d", os.Getpid()))
		cmdDesc = "shell script with child processes"
	}

	if config.Verbose {
		log.Printf("プロセスツリー実行中: %s", cmdDesc)
	}

	err := cmd.Start()
	if err != nil {
		report.AddError(fmt.Errorf("プロセスツリー開始エラー: %w", err))
		report.IncrementFailed()
		return nil
	}

	childPID := cmd.Process.Pid
	report.AddChildPID(childPID)

	if config.Verbose {
		log.Printf("プロセスツリー開始: 親PID %d", childPID)
	}

	err = cmd.Wait()
	if err != nil {
		report.AddError(fmt.Errorf("プロセスツリー実行エラー: %w", err))
		report.IncrementFailed()
	} else {
		report.IncrementSuccess()
		if config.Verbose {
			log.Printf("プロセスツリー完了: 親PID %d", childPID)
		}
	}

	return nil
}